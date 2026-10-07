using System.IO;
using UnityEditor;
using UnityEngine;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Tools > S3 > Build Model Prefabs: wraps each imported model in a P_ prefab that teammates use in their scenes.
    /// The model is a nested prefab instance, so a re-export from Blender updates every P_ prefab automatically.
    /// Existing prefabs are never overwritten (teammates may have added components); delete one to rebuild it.
    /// </summary>
    public static class ArtPrefabBuilder
    {
        const string PrefabsRoot = "Assets/_Project/Prefabs/";

        enum Kind { Gun, Character }

        static readonly (string Model, string Prefab, Kind Kind)[] Entries =
        {
            ("Weapons/SM_Gun_Tier1.fbx", "Weapons/P_Gun_Tier1.prefab", Kind.Gun),   // pistol
            ("Weapons/SM_Gun_Tier2.fbx", "Weapons/P_Gun_Tier2.prefab", Kind.Gun),   // assault rifle
            ("Weapons/SM_Gun_Tier3.fbx", "Weapons/P_Gun_Tier3.prefab", Kind.Gun),   // sniper
            ("Weapons/SM_Gun_Tier4.fbx", "Weapons/P_Gun_Tier4.prefab", Kind.Gun),   // RPG
            ("Characters/SK_Player_Officer.fbx", "Player/P_Player_Officer.prefab", Kind.Character),
        };

        [MenuItem("Tools/S3/Build Model Prefabs")]
        public static void BuildAll()
        {
            int built = 0;
            foreach (var (model, prefab, kind) in Entries)
            {
                if (Build(ArtBudgets.ModelsRoot + model, PrefabsRoot + prefab, kind)) built++;
            }
            Debug.Log($"[Art] Build Model Prefabs: {built} new prefab(s).");
        }

        static bool Build(string modelPath, string prefabPath, Kind kind)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogWarning($"[Art] {modelPath} not found - export it from Blender first.");
                return false;
            }
            if (File.Exists(prefabPath))
            {
                Debug.Log($"[Art] {prefabPath} already exists - skipped.");
                return false;
            }

            // Root = gameplay object (teammates add scripts / Rigidbody / CharacterController here).
            // Child = the model, so swapping the mesh never touches gameplay components.
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            // Characters keep the importer's Animator + Humanoid avatar on the model; the controller is added later.
            if (kind == Kind.Gun) AddFittedBoxCollider(root);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            Object.DestroyImmediate(root);
            if (success) Debug.Log($"[Art] Created {prefabPath}");
            return success;
        }

        // One primitive box instead of a MeshCollider: far cheaper for pickups and thrown guns.
        static void AddFittedBoxCollider(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            // The root sits at the origin with no rotation, so world bounds = local bounds.
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
        }
    }
}
