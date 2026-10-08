using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Tools > S3 > Build Bird Species Variants: one mesh + one rig -> four species.
    /// Each species is a Prefab Variant of P_Bird_Base with its own uniform scale, feather/beak/leg colours and
    /// blend shape weights. Animations, colliders and scripts added to P_Bird_Base reach all four automatically.
    /// The table below is the source of truth: re-running the menu updates existing variants in place
    /// (only scale, materials and blend shapes are touched - components a teammate added are kept).
    /// </summary>
    public static class ArtBirdSpeciesBuilder
    {
        const string BasePrefabPath = "Assets/_Project/Prefabs/Birds/P_Bird_Base.prefab";
        const string VariantFolder = "Assets/_Project/Prefabs/Birds/";
        const string BaseMaterialPrefix = "M_Bird_";
        const string SpeciesMaterialFolder = ArtBudgets.MaterialsRoot + "Birds/Species";

        const string WingsSpread = "Wings_Spread"; // flight pose: wings fully open (birds are always flying)
        const string TailSpread = "Tail_Spread";   // tail fan, used as the per-species shape difference

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        readonly struct Species
        {
            public readonly string Name;
            public readonly float Scale;
            public readonly float TailSpread;   // 0-100
            public readonly float? Smoothness;  // null = keep the base material's value
            public readonly (string Part, string Hex)[] Colours; // Part = base material name without "M_Bird_"; sRGB hex

            public Species(string name, float scale, float tailSpread, float? smoothness, params (string, string)[] colours)
            {
                Name = name;
                Scale = scale;
                TailSpread = tailSpread;
                Smoothness = smoothness;
                Colours = colours;
            }
        }

        // Scale is relative to the base model (gull: 0.6 m beak to tail, 1.66 m wingspan with Wings_Spread = 100).
        // Parts not listed keep the shared base material (eye pupil, claws), which keeps the material count down.
        static readonly Species[] AllSpecies =
        {
            new Species("Gull", 1.0f, 0f, null), // the base model's own colours

            new Species("Pigeon", 0.6f, 50f, null,
                ("Feather_White", "#A7AEBB"), ("Feather_Gray_Light", "#9AA3B0"), ("Feather_Gray", "#7D8796"),
                ("Feather_Gray_Dark", "#4F5663"), ("Feather_Dark", "#2B2E35"),
                ("Beak", "#3A3A3E"), ("Beak_Spot", "#E6E2DA"), ("Beak_Tip", "#2A2A2C"),
                ("Eye_Iris", "#E2571E"), ("Eyelid", "#8A8F98"), ("Legs", "#C9566A")),

            new Species("Crow", 0.8f, 30f, 0.45f, // slightly glossier black plumage
                ("Feather_White", "#2A2B2F"), ("Feather_Gray_Light", "#26282C"), ("Feather_Gray", "#1E1F23"),
                ("Feather_Gray_Dark", "#17181B"), ("Feather_Dark", "#0E0E10"),
                ("Beak", "#141416"), ("Beak_Spot", "#141416"), ("Beak_Tip", "#0E0E10"),
                ("Eye_Iris", "#2B1D14"), ("Eyelid", "#1B1B1E"), ("Legs", "#1A1A1C")),

            new Species("Hawk", 1.2f, 100f, null,
                ("Feather_White", "#E8DCC4"), ("Feather_Gray_Light", "#A8774A"), ("Feather_Gray", "#8A5A35"),
                ("Feather_Gray_Dark", "#5E3B22"), ("Feather_Dark", "#2E1E14"),
                ("Beak", "#3B3B3D"), ("Beak_Spot", "#F2C230"), ("Beak_Tip", "#1C1C1E"),
                ("Eye_Iris", "#F0B020"), ("Eyelid", "#E3B341"), ("Legs", "#F2C230")),
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

            EnsureFolder(SpeciesMaterialFolder);
            // Slot order comes from the base prefab, so re-runs map slots correctly even after materials were swapped.
            Material[] baseMaterials = baseRenderer.sharedMaterials;

            foreach (var species in AllSpecies)
            {
                var materials = BuildMaterials(species, baseMaterials);
                BuildVariant(species, basePrefab, materials);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Art] Build Bird Species Variants: {AllSpecies.Length} variants updated.");
        }

        static Material[] BuildMaterials(Species species, Material[] baseMaterials)
        {
            var colours = new Dictionary<string, string>();
            foreach (var (part, hex) in species.Colours) colours[part] = hex;

            var result = (Material[])baseMaterials.Clone();
            for (int i = 0; i < baseMaterials.Length; i++)
            {
                var baseMaterial = baseMaterials[i];
                if (baseMaterial == null || !baseMaterial.name.StartsWith(BaseMaterialPrefix)) continue;

                string part = baseMaterial.name.Substring(BaseMaterialPrefix.Length);
                if (!colours.TryGetValue(part, out string hex)) continue;
                if (!ColorUtility.TryParseHtmlString(hex, out Color colour))
                {
                    Debug.LogWarning($"[Art] {species.Name}: bad colour {hex} for {part}.");
                    continue;
                }

                result[i] = SpeciesMaterial(species, part, baseMaterial, colour);
            }

            foreach (string part in colours.Keys)
            {
                if (System.Array.Find(baseMaterials, m => m != null && m.name == BaseMaterialPrefix + part) == null)
                    Debug.LogWarning($"[Art] {species.Name}: no slot uses {BaseMaterialPrefix}{part} - colour ignored.");
            }
            return result;
        }

        // M_Bird_<Species>_<Part>: a copy of the base material (same shader + settings) with the species colour.
        // Re-copied from the base every run, so a change to the base material (e.g. smoothness) reaches all species.
        static Material SpeciesMaterial(Species species, string part, Material baseMaterial, Color colour)
        {
            string path = $"{SpeciesMaterialFolder}/{BaseMaterialPrefix}{species.Name}_{part}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(baseMaterial);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.CopyPropertiesFromMaterial(baseMaterial);
            }

            material.SetColor(BaseColorId, colour);
            if (species.Smoothness.HasValue) material.SetFloat(SmoothnessId, species.Smoothness.Value);
            // No GPU instancing: Unity cannot instance SkinnedMeshRenderers. The SRP Batcher batches the birds instead.
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildVariant(Species species, GameObject basePrefab, Material[] materials)
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
                Apply(species, root, materials);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (success) Debug.Log($"[Art] {(exists ? "Updated" : "Created")} {path}");
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        static void Apply(Species species, GameObject root, Material[] materials)
        {
            root.name = $"P_Bird_{species.Name}";

            // Uniform scale on the root: the body CapsuleCollider scales with it, the rig and clips are unaffected.
            root.transform.localScale = Vector3.one * species.Scale;

            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            renderer.sharedMaterials = materials;
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

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
