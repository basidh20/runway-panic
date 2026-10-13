using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Builds the shared M_ materials (URP Lit) from the manifests that export_fbx.py writes next to them:
    /// Art/Materials/Weapons/SM_Gun_Tier1.materials.json -> Art/Materials/Weapons/M_Gun_Tier1_Atlas.mat, ...
    /// Runs automatically when a manifest is imported. Blender is the source of truth: values are overwritten
    /// on every export. Change colours in Blender and re-export, not in the Inspector.
    /// An atlas entry (M_Bird_Atlas, written by bake_atlas.py) names its textures instead of a colour:
    /// baseMap = T_..._Atlas_Albedo, maskMap = T_..._Atlas_Mask (R = metallic, A = smoothness), both looked up in
    /// Art/Textures/&lt;same folder as the manifest&gt;/.
    /// </summary>
    public class ArtMaterialBuilder : AssetPostprocessor
    {
        const string LitShaderName = "Universal Render Pipeline/Lit";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MetallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        static readonly int BlendId = Shader.PropertyToID("_Blend");
        static readonly int PreserveSpecularId = Shader.PropertyToID("_BlendModePreserveSpecular");
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
        static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        // Mirrors the JSON written by export_fbx.py (colours are linear, as Blender stores them).
        [Serializable]
        class Manifest
        {
            public string asset;
            public string source;
            public MaterialEntry[] materials;
        }

        [Serializable]
        class MaterialEntry
        {
            public string name;
            public Color baseColor;
            public float metallic;
            public float roughness;
            public Color emissionColor;
            public float emissionStrength;
            public bool textured;
            public string baseMap; // texture names without extension; empty for flat-colour materials
            public string maskMap;
        }

        static bool IsManifest(string path) =>
            path.StartsWith(ArtBudgets.MaterialsRoot) && path.EndsWith(ArtBudgets.MaterialManifestSuffix);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var modelsToReimport = new List<string>();
            var built = new HashSet<string>();
            foreach (string path in imported)
            {
                if (!IsManifest(path)) continue;
                built.Add(path);
                if (BuildFromManifest(path, out string modelPath) && modelPath != null)
                {
                    modelsToReimport.Add(modelPath);
                }
            }

            // An atlas texture that arrives without its manifest (or after it): build that folder's manifests again,
            // so the atlas material picks the texture up.
            foreach (string path in imported)
            {
                if (!path.StartsWith(ArtBudgets.TexturesRoot)) continue;
                if (!ArtBudgets.IsAtlas(Path.GetFileNameWithoutExtension(path))) continue;

                string folder = ArtBudgets.MaterialsRoot + ArtBudgets.SubFolderOf(path, ArtBudgets.TexturesRoot);
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { folder }))
                {
                    string manifestPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!IsManifest(manifestPath) || !built.Add(manifestPath)) continue;
                    if (BuildFromManifest(manifestPath, out string modelPath) && modelPath != null)
                    {
                        modelsToReimport.Add(modelPath);
                    }
                }
            }

            // A model imported before its materials existed got fallback materials: import it again to pick up the M_ assets.
            // Not from inside this callback: a re-import started here, in the refresh that created the materials, kept
            // the fallback (13 Oct: the hawk stayed light grey until Rebuild Materials From Manifests was run by hand).
            // delayCall runs it once the refresh has finished. That import brings no manifest, so it cannot loop.
            // ArtModelPostprocessor also makes each model depend on its material paths, so Unity re-imports the
            // model by itself when a material appears; this call only makes it happen straight away.
            if (modelsToReimport.Count == 0) return;
            EditorApplication.delayCall += () =>
            {
                foreach (string modelPath in modelsToReimport)
                {
                    AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
                }
            };
        }

        [MenuItem("Tools/S3/Rebuild Materials From Manifests")]
        public static void RebuildAll()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { ArtBudgets.MaterialsRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsManifest(path)) continue;
                BuildFromManifest(path, out string modelPath);
                if (modelPath != null) AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
            }
        }

        /// <returns>True if at least one material was created. modelPath is the matching FBX, if it exists.</returns>
        static bool BuildFromManifest(string manifestPath, out string modelPath)
        {
            modelPath = null;
            Manifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Art] {manifestPath}: cannot read manifest - {e.Message}");
                return false;
            }
            if (manifest?.materials == null) return false;

            var shader = Shader.Find(LitShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Art] Shader '{LitShaderName}' not found - is URP installed?");
                return false;
            }

            string folder = Path.GetDirectoryName(manifestPath).Replace('\\', '/');
            string subFolder = ArtBudgets.SubFolderOf(manifestPath, ArtBudgets.MaterialsRoot);
            string textureFolder = ArtBudgets.TexturesRoot + (subFolder.Length > 0 ? subFolder + "/" : "");
            int created = 0;
            foreach (var entry in manifest.materials)
            {
                string path = $"{folder}/{entry.name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = material == null;
                if (isNew) material = new Material(shader) { name = entry.name };
                else if (material.shader != shader) material.shader = shader;

                Apply(material, entry, textureFolder);
                if (entry.textured && string.IsNullOrEmpty(entry.baseMap))
                {
                    Debug.LogWarning($"[Art] {entry.name}: base colour is textured in Blender - assign the T_ texture by hand.");
                }

                if (isNew)
                {
                    AssetDatabase.CreateAsset(material, path);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
            }

            string candidate = $"{ArtBudgets.ModelsRoot}{subFolder}/{manifest.asset}.fbx";
            if (File.Exists(candidate)) modelPath = candidate;

            Debug.Log($"[Art] {manifest.asset}: {manifest.materials.Length} materials from manifest ({created} new).");
            return created > 0;
        }

        static void Apply(Material material, MaterialEntry entry, string textureFolder)
        {
            // Material colours are stored in sRGB; Unity converts them back to linear (project is Linear).
            Color baseColor = entry.baseColor.gamma;
            baseColor.a = entry.baseColor.a;
            material.SetColor(BaseColorId, baseColor);
            material.SetFloat(MetallicId, entry.metallic);
            material.SetFloat(SmoothnessId, 1f - entry.roughness); // Blender roughness -> Unity smoothness

            // Atlas materials: colour comes from the Albedo; metallic (R) and smoothness (A) from the Mask, which
            // URP Lit reads as its Metallic Map (it then ignores _Metallic and multiplies Mask.a by _Smoothness).
            material.SetTexture(BaseMapId, LoadTexture(textureFolder, entry.baseMap, entry.name));
            Texture2D mask = LoadTexture(textureFolder, entry.maskMap, entry.name);
            material.SetTexture(MetallicGlossMapId, mask);
            if (mask != null) material.EnableKeyword("_METALLICSPECGLOSSMAP");
            else material.DisableKeyword("_METALLICSPECGLOSSMAP");

            Color emission = entry.emissionColor * entry.emissionStrength;
            bool emissive = emission.maxColorComponent > 0.001f;
            material.SetColor(EmissionColorId, emissive ? emission.gamma : Color.black);
            if (emissive) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = emissive
                ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            SetTransparent(material, entry.baseColor.a < 0.999f);
        }

        static Texture2D LoadTexture(string textureFolder, string textureName, string materialName)
        {
            if (string.IsNullOrEmpty(textureName)) return null;
            string path = $"{textureFolder}{textureName}.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"[Art] {materialName}: texture {path} not found.");
            return texture;
        }

        // Same keywords / states URP's material inspector sets for Surface Type Opaque vs Transparent (alpha blend).
        static void SetTransparent(Material material, bool transparent)
        {
            material.SetFloat(SurfaceId, transparent ? 1f : 0f);
            material.SetFloat(BlendId, 0f);
            // Alpha blend with URP's default "Preserve Specular Lighting" on: the shader multiplies the colour by
            // alpha itself (_ALPHAPREMULTIPLY_ON), so the source factor is One, not SrcAlpha. Writing SrcAlpha made
            // Unity rewrite the .mat back to One whenever it validated the material (a changed file on every rebuild).
            if (transparent) material.SetFloat(PreserveSpecularId, 1f);
            if (transparent) material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            else material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetFloat(SrcBlendId, (float)BlendMode.One);
            material.SetFloat(DstBlendId, (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat(SrcBlendAlphaId, (float)BlendMode.One);
            material.SetFloat(DstBlendAlphaId, (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat(ZWriteId, transparent ? 0f : 1f);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            material.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;
            material.SetShaderPassEnabled("DepthOnly", !transparent);
            if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
