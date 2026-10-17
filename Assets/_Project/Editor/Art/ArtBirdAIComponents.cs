using RunwayPanic.Agents;
using UnityEditor;
using UnityEngine;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// F5 of the shared bird AI plan: puts the shared AI components on the bird prefabs.
    /// Root of <c>P_Bird_Base</c> and of <c>P_Bird_Hawk</c> (its own prefab, not a variant): kinematic Rigidbody,
    /// SimpleBirdMotor, BirdHealth, BirdContactAttack, BirdStateLabel; model child: BlendShapeBirdAnimator.
    /// The Gull/Pigeon/Crow variants inherit them and only get their own species asset.
    /// Safe to run again: adds only what is missing and never removes anything (species brains are kept).
    /// </summary>
    public static class ArtBirdAIComponents
    {
        const string BirdsFolder = "Assets/_Project/Prefabs/Birds/";
        const string SpeciesFolder = "Assets/_Project/ScriptableObjects/Birds/";
        const string MeshName = "Bird_Mesh";

        // Prefab name → species asset names to try in order. The gull asset is still called Eagle until the
        // Systems Engineer renames it; the rename keeps its GUID, so the reference stays valid.
        static readonly (string Prefab, string[] Species)[] Variants =
        {
            ("P_Bird_Gull", new[] { "BirdSpecies_Gull", "BirdSpecies_Eagle" }),
            ("P_Bird_Pigeon", new[] { "BirdSpecies_Pigeon" }),
            ("P_Bird_Crow", new[] { "BirdSpecies_Crow" }),
        };

        [MenuItem("Tools/S3/Add Bird AI Components")]
        public static void AddAll()
        {
            var log = new System.Text.StringBuilder("[Art] Add Bird AI Components: ");

            log.Append(Edit("P_Bird_Base", root => AddShared(root), "shared components"));
            foreach (var (prefab, species) in Variants)
                log.Append(Edit(prefab, root => SetSpecies(root, species), "species"));
            log.Append(Edit("P_Bird_Hawk", root => AddShared(root) + ", " + SetSpecies(root, new[] { "BirdSpecies_Hawk" }), "shared components + species"));

            Debug.Log(log.ToString());
        }

        static string Edit(string prefabName, System.Func<GameObject, string> apply, string what)
        {
            string path = BirdsFolder + prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[Art] {path} not found - skipped.");
                return $"{prefabName} MISSING; ";
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                string result = apply(root);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                return success ? $"{prefabName} ({what}: {result}); " : $"{prefabName} SAVE FAILED; ";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static string AddShared(GameObject root)
        {
            int added = 0;

            // Kinematic: the motor moves it. BirdHealth (F3) makes it fall on death by switching this off.
            if (!root.TryGetComponent(out Rigidbody body))
            {
                body = root.AddComponent<Rigidbody>();
                added++;
            }

            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            added += Ensure<SimpleBirdMotor>(root);
            added += Ensure<BirdHealth>(root);
            added += Ensure<BirdContactAttack>(root);
            added += Ensure<BirdStateLabel>(root);
            added += Ensure<BlendShapeBirdAnimator>(FindModelChild(root));
            return $"{added} added";
        }

        static int Ensure<T>(GameObject target) where T : Component
        {
            if (target.GetComponent<T>() != null)
                return 0;
            target.AddComponent<T>();
            return 1;
        }

        // The model child is the object directly under the root that holds Bird_Mesh.
        static GameObject FindModelChild(GameObject root)
        {
            foreach (SkinnedMeshRenderer mesh in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mesh.name != MeshName)
                    continue;
                Transform model = mesh.transform;
                while (model.parent != null && model.parent != root.transform)
                    model = model.parent;
                return model.gameObject;
            }

            Debug.LogWarning($"[Art] {root.name}: no {MeshName} found; BlendShapeBirdAnimator put on the root.");
            return root;
        }

        static string SetSpecies(GameObject root, string[] candidates)
        {
            var health = root.GetComponent<BirdHealth>();
            if (health == null)
                return "no BirdHealth (run on P_Bird_Base first)";

            foreach (string name in candidates)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SpeciesFolder + name + ".asset");
                if (asset == null)
                    continue;

                var serialized = new SerializedObject(health);
                serialized.FindProperty("species").objectReferenceValue = asset;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return name;
            }

            Debug.LogWarning($"[Art] {root.name}: none of {string.Join(", ", candidates)} found.");
            return "species asset not found";
        }
    }
}
