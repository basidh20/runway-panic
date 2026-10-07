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
    /// Art/Materials/Weapons/SM_Gun_Tier1.materials.json -> Art/Materials/Weapons/M_Gun_Polymer.mat, ...
    /// Runs automatically when a manifest is imported. Blender is the source of truth: values are overwritten
    /// on every export. Change colours in Blender and re-export, not in the Inspector.
    /// </summary>
    public class ArtMaterialBuilder : AssetPostprocessor
    {
        const string LitShaderName = "Universal Render Pipeline/Lit";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        static readonly int BlendId = Shader.PropertyToID("_Blend");
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
        }

        static bool IsManifest(string path) =>
            path.StartsWith(ArtBudgets.MaterialsRoot) && path.EndsWith(ArtBudgets.MaterialManifestSuffix);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var modelsToReimport = new List<string>();
            foreach (string path in imported)
            {
                if (!IsManifest(path)) continue;
                if (BuildFromManifest(path, out string modelPath) && modelPath != null)
                {
                    modelsToReimport.Add(modelPath);
                }
            }

            // A model imported before its materials existed got fallback materials: import it again to pick up the M_ assets.
            foreach (string modelPath in modelsToReimport)
            {
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
            }
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
            int created = 0;
            foreach (var entry in manifest.materials)
            {
                string path = $"{folder}/{entry.name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = material == null;
                if (isNew) material = new Material(shader) { name = entry.name };
                else if (material.shader != shader) material.shader = shader;

                Apply(material, entry);
                if (entry.textured)
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

            string subFolder = ArtBudgets.SubFolderOf(manifestPath, ArtBudgets.MaterialsRoot);
            string candidate = $"{ArtBudgets.ModelsRoot}{subFolder}/{manifest.asset}.fbx";
            if (File.Exists(candidate)) modelPath = candidate;

            Debug.Log($"[Art] {manifest.asset}: {manifest.materials.Length} materials from manifest ({created} new).");
            return created > 0;
        }

        static void Apply(Material material, MaterialEntry entry)
        {
            // Material colours are stored in sRGB; Unity converts them back to linear (project is Linear).
            Color baseColor = entry.baseColor.gamma;
            baseColor.a = entry.baseColor.a;
            material.SetColor(BaseColorId, baseColor);
            material.SetFloat(MetallicId, entry.metallic);
            material.SetFloat(SmoothnessId, 1f - entry.roughness); // Blender roughness -> Unity smoothness

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

        // Same keywords / states URP's material inspector sets for Surface Type Opaque vs Transparent (alpha blend).
        static void SetTransparent(Material material, bool transparent)
        {
            material.SetFloat(SurfaceId, transparent ? 1f : 0f);
            material.SetFloat(BlendId, 0f);
            material.SetFloat(SrcBlendId, (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
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
