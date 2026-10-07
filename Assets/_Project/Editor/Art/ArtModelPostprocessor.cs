using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Applies the agreed model import settings to every FBX under Art/Models/ and Art/Animations/,
    /// so all four team members get identical results without touching the Inspector.
    /// Settings are re-applied on every import: Blender is the source of truth, Inspector edits get overwritten.
    /// </summary>
    public class ArtModelPostprocessor : AssetPostprocessor
    {
        // Clip names containing any of these loop; everything else (Dive, Hit, Death, Jump...) plays once.
        static readonly string[] LoopKeywords = { "idle", "walk", "run", "jog", "sprint", "strafe", "fly", "glide", "hover", "loop" };

        // Blender object names are unique per .blend, so the 2nd gun's socket is "Socket_Muzzle.001".
        static readonly Regex BlenderDuplicateSuffix = new Regex(@"\.\d{3}$");

        // Bump when the rules below change so Unity re-imports the affected models.
        public override uint GetVersion() => 2;

        static bool IsModel(string path) => path.StartsWith(ArtBudgets.ModelsRoot);
        static bool IsAnimation(string path) => path.StartsWith(ArtBudgets.AnimationsRoot);

        void OnPreprocessModel()
        {
            if (!IsModel(assetPath) && !IsAnimation(assetPath)) return;

            var importer = (ModelImporter)assetImporter;
            string assetName = Path.GetFileNameWithoutExtension(assetPath);

            // Scene: 1 Blender metre = 1 Unity unit (Blender exports with "FBX All", Unity converts units).
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;

            // Each FBX material slot is mapped by name to the shared M_ asset (URP Lit) that ArtMaterialBuilder
            // creates from the Blender manifest - see OnAssignMaterialModel. No FBX-embedded copies.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            // CPU copy of the mesh is not needed at runtime: halves mesh memory.
            importer.isReadable = false;

            if (IsAnimation(assetPath))
            {
                ConfigureAnimationOnly(importer);
                return;
            }

            if (assetName.StartsWith("SM_"))
            {
                ConfigureStatic(importer);
            }
            else if (assetName.StartsWith("SK_"))
            {
                ConfigureSkinned(importer, assetName);
            }
            else
            {
                Debug.LogWarning($"[Art] {assetPath}: name has no SM_/SK_ prefix - only base import rules applied.");
            }
        }

        static void ConfigureStatic(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.addCollider = false; // primitive colliders are added on the prefab
            importer.meshCompression = ModelImporterMeshCompression.Low;

            // Lightmap UVs only for meshes that can be baked into the level (not held/moving guns).
            string path = importer.assetPath;
            importer.generateSecondaryUV = path.Contains("/Models/Environment/") || path.Contains("/Models/Props/");
        }

        static void ConfigureSkinned(ModelImporter importer, string assetName)
        {
            importer.animationType = assetName.StartsWith("SK_Player")
                ? ModelImporterAnimationType.Human   // Humanoid: Mixamo / retargetable clips
                : ModelImporterAnimationType.Generic; // birds: custom skeleton, not humanoid
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importBlendShapes = true; // species proportions may use shape keys
            importer.addCollider = false;
            importer.meshCompression = ModelImporterMeshCompression.Off; // compression can wobble skinned verts
            importer.generateSecondaryUV = false;
        }

        // Animation-only FBX files (e.g. Mixamo downloads) under Art/Animations/<Player|Birds>/.
        static void ConfigureAnimationOnly(ModelImporter importer)
        {
            importer.animationType = importer.assetPath.Contains("/Animations/Player/")
                ? ModelImporterAnimationType.Human
                : ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.generateSecondaryUV = false;
        }

        void OnPreprocessAnimation()
        {
            if (!IsModel(assetPath) && !IsAnimation(assetPath)) return;

            var importer = (ModelImporter)assetImporter;
            if (!importer.importAnimation) return;

            // Rebuilt from the FBX takes every import, so a new Blender action shows up automatically.
            var clips = importer.defaultClipAnimations;
            if (clips.Length == 0) return;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            foreach (var clip in clips)
            {
                clip.name = CleanClipName(clip.name, fileName);
                clip.loopTime = ShouldLoop(clip.name);
            }
            importer.clipAnimations = clips;
        }

        // Blender names takes "Armature|A_Bird_Fly"; Mixamo names every take "mixamo.com".
        static string CleanClipName(string clipName, string fileName)
        {
            if (clipName == "mixamo.com") return fileName;
            int bar = clipName.LastIndexOf('|');
            return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
        }

        static bool ShouldLoop(string clipName)
        {
            string lower = clipName.ToLowerInvariant();
            foreach (string keyword in LoopKeywords)
            {
                if (lower.Contains(keyword)) return true;
            }
            return false;
        }

        // Called once per FBX material. Returning the existing M_ asset stops Unity generating an embedded copy,
        // so every gun shares one M_Gun_Polymer (fewer materials, SRP Batcher friendly).
        Material OnAssignMaterialModel(Material sourceMaterial, Renderer renderer)
        {
            if (!IsModel(assetPath)) return null;

            string path = ArtBudgets.MaterialPathFor(assetPath, sourceMaterial.name);
            if (!File.Exists(path))
            {
                // Normal on the very first import: ArtMaterialBuilder creates it from the manifest and re-imports this model.
                Debug.LogWarning($"[Art] {Path.GetFileName(assetPath)}: material {sourceMaterial.name} not found at {path}.");
                return null;
            }

            context.DependsOnSourceAsset(path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!IsModel(assetPath)) return;

            // Every asset exposes the same socket names (Socket_Muzzle) to gameplay code. Bones are never renamed.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Socket_")) t.name = BlenderDuplicateSuffix.Replace(t.name, "");
            }

            string assetName = Path.GetFileNameWithoutExtension(assetPath);
            int tris = ArtBudgets.CountTriangles(root);
            int bones = ArtBudgets.CountBones(root);

            if (!ArtBudgets.TryGetModelBudget(assetName, out var budget))
            {
                Debug.LogWarning($"[Art] {assetName}: {tris} tris - no budget defined in ArtBudgets.");
                return;
            }

            if (tris > budget.MaxTris)
            {
                Debug.LogWarning($"[Art] {assetName}: OVER BUDGET {tris} tris > {budget.MaxTris} ({budget.Prefix}).");
            }
            if (budget.MaxBones > 0 && bones > budget.MaxBones)
            {
                Debug.LogWarning($"[Art] {assetName}: OVER BUDGET {bones} bones > {budget.MaxBones} ({budget.Prefix}).");
            }
        }
    }
}
