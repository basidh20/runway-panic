using UnityEngine;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Unity-side source of truth for art budgets and folder rules.
    /// Keep in sync with SourceArt/Tools/export_fbx.py (BUDGETS) and SourceArt/README.md.
    /// </summary>
    public static class ArtBudgets
    {
        public const string ModelsRoot = "Assets/_Project/Art/Models/";
        public const string AnimationsRoot = "Assets/_Project/Art/Animations/";
        public const string TexturesRoot = "Assets/_Project/Art/Textures/";
        public const string MaterialsRoot = "Assets/_Project/Art/Materials/";
        public const string MaterialManifestSuffix = ".materials.json";

        public readonly struct ModelBudget
        {
            public readonly string Prefix;
            public readonly int MaxTris;  // 0 = no triangle limit (count is still reported)
            public readonly int MaxBones; // 0 = no bone limit

            public ModelBudget(string prefix, int maxTris, int maxBones)
            {
                Prefix = prefix;
                MaxTris = maxTris;
                MaxBones = maxBones;
            }
        }

        static readonly ModelBudget[] ModelBudgets =
        {
            new ModelBudget("SK_Bird", 0, 15),  // no tri limit (decided 8 Oct), bones still capped
            new ModelBudget("SK_Player", 8000, 0),
            new ModelBudget("SM_Gun", 2500, 0),
            new ModelBudget("SM_Prop", 500, 0),
        };

        // Max imported size per Textures/<Folder>/. Palette textures (name contains "Palette") use PaletteMaxSize.
        public const int PaletteMaxSize = 256;
        public const int DefaultTextureMaxSize = 1024;

        static readonly (string Folder, int MaxSize)[] TextureFolderBudgets =
        {
            ("Characters", 1024),
            ("Birds", 1024),
            ("Weapons", 512),
            ("Props", 512),
            ("Environment", 2048),
            ("Shared", 1024),
        };

        /// <summary>Longest matching name prefix wins, e.g. "SK_Bird_Hawk" uses the "SK_Bird" budget.</summary>
        public static bool TryGetModelBudget(string assetName, out ModelBudget budget)
        {
            budget = default;
            bool found = false;
            foreach (var candidate in ModelBudgets)
            {
                if (!assetName.StartsWith(candidate.Prefix)) continue;
                if (found && candidate.Prefix.Length <= budget.Prefix.Length) continue;
                budget = candidate;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Materials sit in the folder that mirrors the model's: Models/Weapons/SM_Gun_Tier1.fbx -> Materials/Weapons/M_Gun_Polymer.mat.
        /// </summary>
        public static string MaterialPathFor(string modelPath, string materialName)
        {
            string subFolder = SubFolderOf(modelPath, ModelsRoot);
            return MaterialsRoot + (subFolder.Length > 0 ? subFolder + "/" : "") + materialName + ".mat";
        }

        /// <summary>Models/Weapons/X.fbx with root Models/ -> "Weapons" (empty if the file sits directly in root).</summary>
        public static string SubFolderOf(string assetPath, string root)
        {
            if (!assetPath.StartsWith(root)) return "";
            string relative = assetPath.Substring(root.Length);
            int slash = relative.IndexOf('/');
            return slash < 0 ? "" : relative.Substring(0, slash);
        }

        public static bool IsPalette(string textureName) => textureName.Contains("Palette");

        public static int TextureMaxSizeFor(string assetPath, string textureName)
        {
            if (IsPalette(textureName)) return PaletteMaxSize;
            if (!assetPath.StartsWith(TexturesRoot)) return DefaultTextureMaxSize;

            string relative = assetPath.Substring(TexturesRoot.Length);
            int slash = relative.IndexOf('/');
            if (slash < 0) return DefaultTextureMaxSize;

            string folder = relative.Substring(0, slash);
            foreach (var (name, maxSize) in TextureFolderBudgets)
            {
                if (name == folder) return maxSize;
            }
            return DefaultTextureMaxSize;
        }

        /// <summary>Triangles across every MeshFilter and SkinnedMeshRenderer under root.</summary>
        public static int CountTriangles(GameObject root)
        {
            int tris = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                tris += CountTriangles(filter.sharedMesh);
            }
            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                tris += CountTriangles(skinned.sharedMesh);
            }
            return tris;
        }

        public static int CountTriangles(Mesh mesh)
        {
            if (mesh == null) return 0;
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                indices += mesh.GetIndexCount(i);
            }
            return (int)(indices / 3);
        }

        /// <summary>Largest bone count of any SkinnedMeshRenderer under root.</summary>
        public static int CountBones(GameObject root)
        {
            int bones = 0;
            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bones = Mathf.Max(bones, skinned.bones.Length);
            }
            return bones;
        }
    }
}
