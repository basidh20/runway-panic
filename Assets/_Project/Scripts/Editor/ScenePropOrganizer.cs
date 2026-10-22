using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Groups only loose, explicitly recognised scenery prefabs; never gameplay systems.</summary>
    public static class ScenePropOrganizer
    {
        private static readonly Dictionary<string, string> Groups = new Dictionary<string, string>
        {
            { "Assets/_Project/Prefabs/Environment/Aircraft/P_Prop_Aircraft_WhiteRed.prefab", "Aircraft" },
            { "Assets/OlegWER/AbandonedAircraft/Prefabs/SM_aircraft_.prefab", "Aircraft" },
            { "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Vehicles/ForestFireTruck1.prefab", "Emergency_Vehicles" },
            { "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Vehicles/ForestFireTruck3.prefab", "Emergency_Vehicles" },
            { "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Equipment/TrafficCone.prefab", "Safety_Equipment/Traffic_Cones" },
            { "Assets/Free_Street_Props/PBR_Prefabs/firehydrant_01_a_02.prefab", "Safety_Equipment/Fire_Hydrants" },
            { "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Equipment/ExtensionCord.prefab", "Safety_Equipment/Cord_Reels" },
            { "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Enviroment/Bush1.prefab", "Vegetation" }
        };

        [MenuItem("Tools/Runway Panic/Organise Loose World Props")]
        private static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var loose = scene.GetRootGameObjects().Where(g => Groups.ContainsKey(Source(g))).ToArray();
            if (loose.Length == 0)
            {
                Debug.Log("[PropHierarchy] No recognised loose props remain; nothing changed.");
                return;
            }
            var previousRoots = scene.GetRootGameObjects();
            int colliderCount = previousRoots.Sum(g => g.GetComponentsInChildren<Collider>(true).Length);
            var transforms = loose.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var matrices = transforms.Select(t => t.localToWorldMatrix).ToArray();
            var prefabSources = loose.Select(Source).ToArray();
            var active = loose.Select(g => g.activeInHierarchy).ToArray();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Organise loose world props");
            try
            {
                // Environment has an inherited NavMeshModifier. Keep these grouping nodes separate
                // so navigation source eligibility and existing baked data are not changed.
                var root = previousRoots.SingleOrDefault(g => g.name == "World_Props");
                if (root == null)
                {
                    root = new GameObject("World_Props");
                    Undo.RegisterCreatedObjectUndo(root, "Create world props group");
                    SceneManager.MoveGameObjectToScene(root, scene);
                }
                RequireIdentity(root.transform);
                foreach (var prop in loose)
                {
                    var parent = root.transform;
                    foreach (string segment in Groups[Source(prop)].Split('/'))
                    {
                        var child = parent.Find(segment);
                        if (child == null)
                        {
                            var go = new GameObject(segment);
                            Undo.RegisterCreatedObjectUndo(go, "Create prop category");
                            go.transform.SetParent(parent, false);
                            child = go.transform;
                        }
                        RequireIdentity(child);
                        parent = child;
                    }
                    Undo.SetTransformParent(prop.transform, parent, "Group scenery prefab");
                    PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);
                }
                for (int i = 0; i < transforms.Length; i++)
                    for (int j = 0; j < 16; j++)
                        if (Mathf.Abs(transforms[i].localToWorldMatrix[j] - matrices[i][j]) > .0001f)
                            throw new InvalidOperationException("World transform changed for " + transforms[i].name);
                for (int i = 0; i < loose.Length; i++)
                    if (Source(loose[i]) != prefabSources[i] || loose[i].activeInHierarchy != active[i])
                        throw new InvalidOperationException("Prefab source or active state changed for " + loose[i].name);
                if (scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Collider>(true).Length) != colliderCount)
                    throw new InvalidOperationException("Collider count changed unexpectedly.");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save organised Main.");
                var report = new StringBuilder("# Main scene prop organisation\n\n");
                report.AppendLine($"Moved {loose.Length} loose prefab instances into `World_Props`, grouped by type.\n");
                foreach (var group in loose.GroupBy(g => Groups[Source(g)]))
                {
                    report.AppendLine("## " + group.Key + "\n");
                    foreach (var prop in group) report.AppendLine("- `" + prop.name + "`");
                    report.AppendLine();
                }
                report.AppendLine($"Verified {transforms.Length} world-space transforms, {loose.Length} prefab links/active states, and unchanged total collider count ({colliderCount}).");
                report.AppendLine("\nObject names, placement, rotation, size, components, materials, layers and gameplay references are retained. No assets were deleted or unpacked. No navigation or lighting bake was run.");
                report.AppendLine("\n`World_Props` is separate from `Environment` because Environment carries an inherited navigation modifier. Existing organised environment groups and gameplay/system roots were left untouched.");
                report.AppendLine("\nFor future scenery: drag the prefab into its matching World_Props category and position it normally. Do not move or scale the grouping objects; keep their transforms at position/rotation zero and scale one. Leave player, waves, shop and bird systems outside the scenery group.");
                File.WriteAllText("Docs/WorldBuilder/SceneHierarchy.md", report.ToString());
                Selection.activeGameObject = root;
                Debug.Log($"[PropHierarchy] Grouped {loose.Length} loose props. Verified unchanged world transforms, prefab links, active states and collider count; saved Main.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
            finally { Undo.CollapseUndoOperations(undo); }
        }

        private static string Source(GameObject go) => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) ?? "";

        private static void RequireIdentity(Transform group)
        {
            if (group.localPosition != Vector3.zero || Quaternion.Angle(group.localRotation, Quaternion.identity) > .001f ||
                group.localScale != Vector3.one || !group.gameObject.activeInHierarchy || group.GetComponents<Component>().Length != 1)
                throw new InvalidOperationException("Expected active, transform-only identity group: " + group.name);
        }
    }
}
