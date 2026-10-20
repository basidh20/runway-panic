using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    public static class GullG2Setup
    {
        [MenuItem("Tools/IS/Gull/Connect G2 Supply in Gull Sandbox")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != GullG1Setup.SandboxPath || scene.isDirty)
                throw new InvalidOperationException("Open and save the Gull sandbox outside Play first.");
            var config = AssetDatabase.LoadAssetAtPath<GullStrategyConfig>(GullG1Setup.ConfigPath);
            var species = AssetDatabase.LoadAssetAtPath<RunwayPanic.Data.BirdSpeciesData>("Assets/_Project/ScriptableObjects/Birds/BirdSpecies_Gull.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullFodSetup.PrefabPath);
            var points = UnityEngine.Object.FindObjectsByType<BirdPoint>(FindObjectsSortMode.None).Where(point => point.Type == BirdPointType.FodSpawn).ToArray();
            var managers = UnityEngine.Object.FindObjectsByType<FodManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (config == null || species == null || prefab == null || points.Length != 8 || managers.Length > 1)
                throw new InvalidOperationException("Expected Gull assets, eight existing FOD markers and at most one manager.");
            var contents = PrefabUtility.LoadPrefabContents(GullG1Setup.PrefabPath);
            try
            {
                var brain = contents.GetComponent<GullBrain>();
                if (brain == null) throw new InvalidOperationException("G1 Gull brain is missing.");
                var settings = new SerializedObject(brain);
                settings.FindProperty("previewStateCycle").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, GullG1Setup.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Connect Gull sandbox supply");
            try
            {
                foreach (var point in points)
                {
                    var supply = point.GetComponent<FodSpawnPoint>();
                    if (supply == null) supply = Undo.AddComponent<FodSpawnPoint>(point.gameObject);
                    var data = new SerializedObject(supply);
                    data.FindProperty("prefab").objectReferenceValue = prefab.GetComponent<FodItem>();
                    data.FindProperty("config").objectReferenceValue = config;
                    data.FindProperty("species").objectReferenceValue = species;
                    data.ApplyModifiedProperties();
                }
                FodManager manager;
                if (managers.Length == 0)
                {
                    var root = new GameObject("FodManager");
                    Undo.RegisterCreatedObjectUndo(root, "Add FOD manager");
                    manager = Undo.AddComponent<FodManager>(root);
                }
                else manager = managers[0];
                var managerData = new SerializedObject(manager);
                managerData.FindProperty("config").objectReferenceValue = config;
                managerData.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Gull sandbox.");
                AssetDatabase.SaveAssets();
                Debug.Log("[GullG2] Eight supplies and one manager saved. Play/B now spawns collecting Gulls, which wait at JoinKettle for G3.");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }
    }
}
