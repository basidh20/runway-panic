using System;
using System.Linq;
using RunwayPanic.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>Explicit, one-time G1 asset/sandbox setup; never edits Main or the base/other species prefabs.</summary>
    public static class GullG1Setup
    {
        public const string SandboxPath = "Assets/_Project/Scenes/Sandbox/Sandbox_IS_Gull.unity";
        public const string ConfigPath = "Assets/_Project/ScriptableObjects/Birds/Strategy/GullStrategyConfig.asset";
        public const string PrefabPath = "Assets/_Project/Prefabs/Birds/P_Bird_Gull.prefab";
        private const string MainPath = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("Tools/IS/Gull/Set Up G1 Sandbox and Prefab")]
        public static void SetUp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxPath) != null)
                throw new InvalidOperationException("Save/stop first. This one-time setup refuses to overwrite an existing Gull sandbox.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponent<BirdHealth>() == null || prefab.GetComponent<IBirdMotor>() == null ||
                prefab.GetComponents<BirdBrain>().Length != 0 || AssetDatabase.LoadAssetAtPath<SceneAsset>(MainPath) == null)
                throw new InvalidOperationException("Expected Main and a foundation-ready Gull prefab without an existing brain.");

            var config = AssetDatabase.LoadAssetAtPath<GullStrategyConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GullStrategyConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var brain = contents.AddComponent<GullBrain>();
                var data = new SerializedObject(brain);
                data.FindProperty("config").objectReferenceValue = config;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
            if (!AssetDatabase.CopyAsset(MainPath, SandboxPath)) throw new InvalidOperationException("Could not copy Main into a new Gull sandbox.");
            var scene = EditorSceneManager.OpenScene(SandboxPath, OpenSceneMode.Single);
            var root = new GameObject("GullTestSpawner_G1");
            var spawner = root.AddComponent<BirdTestSpawner>();
            var settings = new SerializedObject(spawner);
            settings.FindProperty("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            settings.FindProperty("count").intValue = 5;
            settings.FindProperty("spawnOnStart").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (UnityEngine.Object.FindObjectsByType<FlightGrid>(FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<BirdPoint>(FindObjectsSortMode.None).Count(point => point.Type == BirdPointType.KettleCenter) != 1)
                throw new InvalidOperationException("Copied scene is missing its single flight grid or kettle centre. Review before using it.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Gull sandbox.");
            Selection.activeGameObject = root;
            Debug.Log("[GullG1] Sandbox ready: Play, then B spawns five Gull state previews; F3 toggles labels. Main unchanged.");
        }
    }
}
