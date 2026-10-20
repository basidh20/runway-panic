using System;
using RunwayPanic.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    public static class GullFodSetup
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Birds/FOD/P_FOD_Debris.prefab";

        [MenuItem("Tools/IS/Gull/Create FOD Debris Prefab")]
        public static void CreatePrefab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                throw new InvalidOperationException("Stop Play first. This tool refuses to overwrite an existing debris prefab.");
            var config = AssetDatabase.LoadAssetAtPath<GullStrategyConfig>(GullG1Setup.ConfigPath);
            var species = AssetDatabase.LoadAssetAtPath<BirdSpeciesData>("Assets/_Project/ScriptableObjects/Birds/BirdSpecies_Gull.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Greybox/M_Greybox_Metal.mat");
            if (config == null || species == null || material == null) throw new InvalidOperationException("Missing Gull config/species or greybox metal material.");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs/Birds/FOD")) AssetDatabase.CreateFolder("Assets/_Project/Prefabs/Birds", "FOD");
            var root = new GameObject("P_FOD_Debris");
            try
            {
                var body = root.AddComponent<Rigidbody>();
                body.mass = 2;
                body.isKinematic = true;
                body.useGravity = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(.3f, .15f, .4f);
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = box.size;
                obstacle.carving = true;
                obstacle.enabled = false;
                var item = root.AddComponent<FodItem>();
                var data = new SerializedObject(item);
                data.FindProperty("config").objectReferenceValue = config;
                data.FindProperty("species").objectReferenceValue = species;
                data.ApplyModifiedPropertiesWithoutUndo();
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "DebrisVisual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = box.size;
                UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.GetComponent<Renderer>().sharedMaterial = material;
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null) throw new InvalidOperationException("Could not save debris prefab.");
                AssetDatabase.SaveAssets();
                Debug.Log("[GullFOD] Created 0.3 x 0.15 x 0.4 m, mass-2 debris prefab with shared Gull references.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/IS/Gull/Open Gull Sandbox")]
        public static void OpenSandbox()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop and save the current scene first.");
            EditorSceneManager.OpenScene(GullG1Setup.SandboxPath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/IS/Gull/Drop FOD Above Player (Play)")]
        public static void DebugDrop()
        {
            if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().path != GullG1Setup.SandboxPath)
                throw new InvalidOperationException("Run this only in Play in the Gull sandbox.");
            var player = UnityEngine.Object.FindAnyObjectByType<RunwayPanic.Player.PlayerHealth>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (player == null || prefab == null) throw new InvalidOperationException("Expected a player and the FOD prefab.");
            var item = UnityEngine.Object.Instantiate(prefab, player.transform.position + Vector3.up * 4, Quaternion.identity).GetComponent<FodItem>();
            item.Release();
            Selection.activeGameObject = item.gameObject;
        }
    }
}
