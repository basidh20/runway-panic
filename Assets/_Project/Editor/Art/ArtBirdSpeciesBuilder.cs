using System.IO;
using UnityEditor;
using UnityEngine;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Tools > S3 > Build Bird Species Variants: one mesh + one rig -> Gull, Pigeon, Crow; plus the Hawk's own model.
    /// Recoloured species are Prefab Variants of P_Bird_Base with their own uniform scale, atlas material and
    /// blend shape weights. Animations, colliders and scripts added to P_Bird_Base reach all of them automatically.
    /// "Own model" species (Hawk: SK_Bird_Hawk, same bone names) keep their prefab from Build Model Prefabs and only
    /// get their scale + blend shapes here, so every bird starts in the same flying pose.
    /// Species colours are NOT set here: SourceArt/Tools/bird_species.json holds them, and export_fbx.py fills them
    /// into one Albedo texture per species on the gull's UVs (M_Bird_Pigeon_Atlas, M_Bird_Crow_Atlas). So every
    /// species is 1 material and 1 submesh instead of 13.
    /// The table below is the source of truth for scale and shape: re-running the menu updates existing prefabs in
    /// place (only scale, material and blend shapes are touched - components a teammate added are kept).
    /// </summary>
    public static class ArtBirdSpeciesBuilder
    {
        const string BasePrefabPath = "Assets/_Project/Prefabs/Birds/P_Bird_Base.prefab";
        const string VariantFolder = "Assets/_Project/Prefabs/Birds/";
        const string MaterialFolder = ArtBudgets.MaterialsRoot + "Birds/";

        const string WingsSpread = "Wings_Spread"; // flight pose: wings fully open (birds are always flying)
        const string TailSpread = "Tail_Spread";   // tail fan, used as the per-species shape difference

        readonly struct Species
        {
            public readonly string Name;
            public readonly float Scale;
            public readonly float TailSpread; // 0-100
            public readonly string Material;  // atlas material in Materials/Birds; null = own model, keeps its own

            public Species(string name, float scale, float tailSpread, string material)
            {
                Name = name;
                Scale = scale;
                TailSpread = tailSpread;
                Material = material;
            }

            public bool OwnModel => Material == null;
        }

        // Scale is relative to the base model (gull: 0.6 m beak to tail, 1.66 m wingspan with Wings_Spread = 100).
        static readonly Species[] AllSpecies =
        {
            new Species("Gull", 1.0f, 0f, "M_Bird_Atlas"),           // the base model's own colours
            new Species("Pigeon", 0.6f, 50f, "M_Bird_Pigeon_Atlas"), // blue-grey, pink legs
            new Species("Crow", 0.8f, 30f, "M_Bird_Crow_Atlas"),     // black, slightly glossier plumage

            // Modelled separately (11 Oct) at real size: 0.56 m beak to tail, 1.51 m wingspan; colours from Blender.
            // Scaled 1.3 (18 Oct): 0.73 m, 1.96 m wingspan, so the wave-4 boss reads as the biggest bird (gull 1.66 m).
            new Species("Hawk", 1.3f, 100f, null),
        };

        [MenuItem("Tools/S3/Build Bird Species Variants")]
        public static void BuildAll()
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
            var baseRenderer = basePrefab ? basePrefab.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (baseRenderer == null)
            {
                Debug.LogWarning($"[Art] {BasePrefabPath} (with a SkinnedMeshRenderer) not found - run Build Model Prefabs first.");
                return;
            }
            if (baseRenderer.sharedMesh.subMeshCount != 1)
            {
                Debug.LogWarning($"[Art] SK_Bird has {baseRenderer.sharedMesh.subMeshCount} submeshes, expected 1 - " +
                                 "export it from Blender again (export_fbx.py merges the materials into the atlas).");
                return;
            }

            int updated = 0;
            foreach (var species in AllSpecies)
            {
                if (species.OwnModel)
                {
                    if (UpdateOwnModel(species)) updated++;
                    continue;
                }

                string materialPath = $"{MaterialFolder}{species.Material}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Debug.LogWarning($"[Art] {species.Name}: {materialPath} not found - export SK_Bird from Blender first.");
                    continue;
                }
                if (BuildVariant(species, basePrefab, material)) updated++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Art] Build Bird Species Variants: {updated} of {AllSpecies.Length} species updated.");
        }

        static bool BuildVariant(Species species, GameObject basePrefab, Material material)
        {
            string path = $"{VariantFolder}P_Bird_{species.Name}.prefab";
            bool exists = File.Exists(path);

            // New: an instance of P_Bird_Base saved as a new asset becomes a Prefab Variant.
            // Existing: edit its contents so teammates' components on the variant are kept.
            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(path)
                : (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                Apply(species, root, material);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (success) Debug.Log($"[Art] {(exists ? "Updated" : "Created")} {path}");
                return success;
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        // Own-model species: the prefab comes from Build Model Prefabs; only scale + blend shapes are set here.
        static bool UpdateOwnModel(Species species)
        {
            string path = $"{VariantFolder}P_Bird_{species.Name}.prefab";
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Art] {path} not found - run Build Model Prefabs first.");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Apply(species, root, null);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (success) Debug.Log($"[Art] Updated {path} (own model)");
                return success;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void Apply(Species species, GameObject root, Material material)
        {
            root.name = $"P_Bird_{species.Name}";

            // Uniform scale on the root: the body CapsuleCollider scales with it, the rig and clips are unaffected.
            root.transform.localScale = Vector3.one * species.Scale;

            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            // No GPU instancing: Unity cannot instance SkinnedMeshRenderers. The SRP Batcher batches the birds instead.
            if (material != null)
            {
                // Back to the model's own single slot first: an older variant still carries one override per old
                // slot (13), and assigning a shorter array only rewrites slot 0, leaving 12 dead references.
                using (var serialized = new SerializedObject(renderer))
                {
                    PrefabUtility.RevertPropertyOverride(serialized.FindProperty("m_Materials"), InteractionMode.AutomatedAction);
                }
                renderer.sharedMaterials = new[] { material };
            }
            SetBlendShape(renderer, WingsSpread, 100f, species.Name);
            SetBlendShape(renderer, TailSpread, species.TailSpread, species.Name);
        }

        static void SetBlendShape(SkinnedMeshRenderer renderer, string shape, float weight, string species)
        {
            int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
            if (index < 0)
            {
                Debug.LogWarning($"[Art] {species}: blend shape {shape} not found on {renderer.sharedMesh.name}.");
                return;
            }
            renderer.SetBlendShapeWeight(index, weight);
        }
    }
}
