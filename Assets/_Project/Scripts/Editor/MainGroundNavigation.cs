using System;
using System.IO;
using System.Linq;
using RunwayPanic.Interaction;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Main ground navigation setup and refresh with explicit scenery/dynamic exclusions.</summary>
    public static class MainGroundNavigation
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string DataPath = "Assets/_Project/Navigation/MainGroundNavMesh.asset";
        private static bool refreshInProgress;
        private static readonly string[] FinalSceneryPrefabs =
        {
            "Assets/_Project/Prefabs/Environment/P_Prop_ControlTower.prefab",
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Equipment/TrafficCone.prefab",
            "Assets/Free_Street_Props/PBR_Prefabs/firehydrant_01_a_02.prefab",
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Equipment/ExtensionCord.prefab",
            "Assets/OlegWER/AbandonedAircraft/Prefabs/SM_aircraft_.prefab",
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Vehicles/ForestFireTruck1.prefab",
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Vehicles/ForestFireTruck3.prefab"
        };

        private static GameObject[] SourceRoots()
        {
            var scene = SceneManager.GetActiveScene();
            var environment = GameObject.Find("Environment");
            if (environment == null || environment.scene != scene)
                throw new InvalidOperationException("Main Environment was not found in the active scene.");
            var props = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.gameObject.scene == scene && !t.IsChildOf(environment.transform) &&
                            PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) &&
                            FinalSceneryPrefabs.Contains(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)))
                .Select(t => t.gameObject)
                .Where(g => g.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger));
            return new[] { environment }.Concat(props).Distinct().ToArray();
        }

        private static GameObject DynamicRoot(Collider collider)
        {
            return collider.GetComponentInParent<Door>()?.gameObject ??
                   collider.GetComponentInParent<NavMeshObstacle>()?.gameObject ??
                   collider.GetComponentInParent<Rigidbody>()?.gameObject ??
                   collider.GetComponentInParent<NavMeshAgent>()?.gameObject ??
                   (collider.isTrigger || collider is CharacterController ? collider.gameObject : null);
        }

        private static Collider[] Sources()
        {
            return SourceRoots().SelectMany(g => g.GetComponentsInChildren<Collider>()).Distinct()
                .Where(c => c.enabled && c.gameObject.activeInHierarchy && DynamicRoot(c) == null)
                .ToArray();
        }

        private static GameObject[] Exclusions()
        {
            return SourceRoots().SelectMany(g => g.GetComponentsInChildren<Collider>())
                .Where(c => c.enabled && c.gameObject.activeInHierarchy)
                .Select(DynamicRoot).Where(g => g != null)
                .Distinct().ToArray();
        }

        [MenuItem("Tools/Runway Panic/Inspect Main Ground Navigation Sources")]
        public static void Inspect()
        {
            var sources = Sources();
            Debug.Log("[GroundNav] Static colliders: " + sources.Length + "; excluded roots:\n" +
                      string.Join("\n", Exclusions().Select(c => PathOf(c.transform))) +
                      "\nCandidate scenery roots (not proof of a refreshed bake):\n" +
                      string.Join("\n", SourceRoots().Select(g => PathOf(g.transform))));
        }

        [MenuItem("Tools/Runway Panic/Refresh Main Ground Navigation")]
        public static void Refresh()
        {
            var scene = SceneManager.GetActiveScene();
            if (refreshInProgress || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
                scene.path != ScenePath || scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open only saved Main outside Play mode and wait for compilation/baking.");
            var surface = GameObject.Find("GroundNavigation")?.GetComponent<NavMeshSurface>();
            if (surface == null || surface.navMeshData == null ||
                AssetDatabase.GetAssetPath(surface.navMeshData) != DataPath ||
                surface.collectObjects != CollectObjects.MarkedWithModifier ||
                surface.useGeometry != NavMeshCollectGeometry.PhysicsColliders)
                throw new InvalidOperationException("Expected marked-collider ground navigation setup was not found; inspect before changing it.");
            if (!Sources().Any(c => c.name == "Ground_100x80"))
                throw new InvalidOperationException("Expected airport ground collider not found.");

            // Validate before adding anything. Never rewrite a teammate's existing modifier settings.
            var roots = SourceRoots();
            var excluded = Exclusions();
            foreach (var root in roots.Concat(excluded).Distinct())
            {
                var modifier = root.GetComponent<NavMeshModifier>();
                bool ignore = excluded.Contains(root);
                if (modifier != null && (!modifier.enabled || !modifier.applyToChildren ||
                    modifier.ignoreFromBuild != ignore || !modifier.AffectsAgentType(surface.agentTypeID)))
                    throw new InvalidOperationException("Conflicting navigation modifier: " + PathOf(root.transform));
            }
            Undo.SetCurrentGroupName("Refresh airport navigation sources");
            foreach (var root in roots.Concat(excluded).Distinct())
            {
                if (root.GetComponent<NavMeshModifier>() != null) continue;
                var modifier = Undo.AddComponent<NavMeshModifier>(root);
                modifier.applyToChildren = true;
                modifier.ignoreFromBuild = excluded.Contains(root);
            }

            // Update in place: keep the asset GUID, surface reference and agent/bake settings.
            var data = surface.navMeshData;
            refreshInProgress = true;
            try
            {
                var operation = surface.UpdateNavMesh(data);
                operation.completed += _ =>
                {
                    try
                    {
                        EditorUtility.SetDirty(data);
                        AssetDatabase.SaveAssetIfDirty(data);
                        if (scene.isLoaded)
                        {
                            EditorSceneManager.MarkSceneDirty(scene);
                            if (!EditorSceneManager.SaveScene(scene))
                                throw new InvalidOperationException("Could not save refreshed Main.");
                        }
                        Debug.Log("[GroundNav] Refresh completed; saved existing NavMesh asset and scenery modifiers. Run Check Main Ground Navigation and visually review prop footprints.");
                    }
                    finally { refreshInProgress = false; }
                };
            }
            catch { refreshInProgress = false; throw; }
        }

        [MenuItem("Tools/Runway Panic/Bake Main Ground Navigation")]
        public static void Bake()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            if (GameObject.Find("GroundNavigation") != null || File.Exists(DataPath) || AssetDatabase.LoadMainAssetAtPath(DataPath) != null ||
                UnityEngine.Object.FindObjectsByType<NavMeshModifier>().Length != 0)
                throw new InvalidOperationException("Existing navigation setup found; inspect it before rebaking.");
            var sources = Sources();
            if (sources.Length == 0 || !sources.Any(c => c.name == "Ground_100x80"))
                throw new InvalidOperationException("Expected airport ground collider not found.");

            foreach (var sourceRoot in SourceRoots())
            {
                var include = Undo.AddComponent<NavMeshModifier>(sourceRoot);
                include.applyToChildren = true;
            }
            foreach (var excluded in Exclusions())
            {
                var modifier = Undo.AddComponent<NavMeshModifier>(excluded);
                modifier.ignoreFromBuild = true;
                modifier.applyToChildren = true;
            }
            var root = new GameObject("GroundNavigation");
            Undo.RegisterCreatedObjectUndo(root, "Create ground navigation");
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.MarkedWithModifier;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.agentTypeID = 0;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("Ground NavMesh build failed.");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Navigation"))
                AssetDatabase.CreateFolder("Assets/_Project", "Navigation");
            AssetDatabase.CreateAsset(surface.navMeshData, DataPath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main.");
            Debug.Log($"[GroundNav] Baked and saved ground navigation from {sources.Length} static colliders: {DataPath}");
        }

        [MenuItem("Tools/Runway Panic/Check Main Ground Navigation")]
        public static void Check()
        {
            var surface = GameObject.Find("GroundNavigation")?.GetComponent<NavMeshSurface>();
            if (surface == null || surface.navMeshData == null)
                throw new InvalidOperationException("Ground navigation is not present.");
            bool Sample(Vector3 p) => NavMesh.SamplePosition(p, out _, 0.3f, NavMesh.AllAreas);
            string Route(Vector3 a, Vector3 b)
            {
                var path = new NavMeshPath();
                bool ok = NavMesh.SamplePosition(a, out var start, 0.5f, NavMesh.AllAreas) &&
                          NavMesh.SamplePosition(b, out var end, 0.5f, NavMesh.AllAreas) &&
                          NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path);
                return ok ? path.status.ToString() : "No sample/path";
            }
            Debug.Log("[GroundNavCheck] runway=" + Sample(new Vector3(0, 0, -24)) +
                      "; shelter A floor=" + Sample(new Vector3(-21, 0, 12)) +
                      "; shelter B floor=" + Sample(new Vector3(21, 0, -12)) +
                      "; shelter A back wall=" + Sample(new Vector3(-24.8f, 0, 12)) +
                      "; A route=" + Route(new Vector3(-14, 0, 12), new Vector3(-21, 0, 12)) +
                      "; B route=" + Route(new Vector3(14, 0, -12), new Vector3(21, 0, -12)));
        }

        private static string PathOf(Transform item)
        {
            string path = item.name;
            while (item.parent != null) { item = item.parent; path = item.name + "/" + path; }
            return path;
        }
    }
}
