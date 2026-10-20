using System.Linq;
using RunwayPanic.Weapons;
using UnityEditor;
using UnityEngine;

namespace RunwayPanic.PlayerTools
{
    /// <summary>Imports the user-supplied PSX rig, preserving its animation hierarchy and source files.</summary>
    public static class PSXFirstPersonArmsBuilder
    {
        private const string ModelPath = "Assets/_Project/Art/Models/Characters/FirstPerson/PSXArms/arms_rig.fbx";
        private const string AnimFolder = "Assets/_Project/Art/Animations/Player/FirstPerson/PSXArms";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Characters/FirstPerson/PSXArms";
        private const string ResourceFolder = "Assets/_Project/Resources/FirstPerson";

        [MenuItem("Tools/S2/Build PSX First Person Arms")]
        public static string Build()
        {
            EnsureFolder(AnimFolder); EnsureFolder(MaterialFolder); EnsureFolder(ResourceFolder);
            Material gloves = Material("M_PSX_Arms_Gloves", "arms_gloves_01");
            Material("M_PSX_Arms_Bare", "arms_01");
            Material magazine = AssetDatabase.LoadAssetAtPath<Material>(ResourceFolder + "/M_FP_Magazine.mat");
            if (magazine == null)
            {
                magazine = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                magazine.SetColor("_BaseColor", new Color(0.045f, 0.052f, 0.06f));
                magazine.SetFloat("_Metallic", 0.65f); magazine.SetFloat("_Smoothness", 0.32f);
                AssetDatabase.CreateAsset(magazine, ResourceFolder + "/M_FP_Magazine.mat");
            }
            var suppliedClips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            foreach (var source in suppliedClips)
            {
                var clip = Object.Instantiate(source);
                clip.name = "A_FP_PSX_" + source.name;
                clip.legacy = false;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = source.name.Contains("idle") || source.name == "relax";
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                Store(clip, AnimFolder + "/" + clip.name + ".anim");
            }
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            root.name = "PSX_FirstPersonArms";
            try
            {
                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                AnimationClip idle = Clip("finger_gun_idle"), left = Clip("grab.L"), right = Clip("grab.R");
                idle.SampleAnimation(root, idle.length * 0.55f);
                Quaternion rightGrip = Find(root, "hand.R").rotation;
                left.SampleAnimation(root, left.length * 0.55f);
                Quaternion leftGrip = Find(root, "hand.L").rotation;
                right.SampleAnimation(root, right.length * 0.55f);
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    renderer.sharedMaterials = Enumerable.Repeat(gloves, renderer.sharedMesh.subMeshCount).ToArray();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.updateWhenOffscreen = true;
                    renderer.receiveShadows = true;
                }
                AvatarMask leftMask = Mask(root, "LeftFingers", false);
                AvatarMask triggerMask = Mask(root, "TriggerFinger", true);
                root.AddComponent<FirstPersonArms>().ConfigureSuppliedRig(root.transform, rightGrip, leftGrip,
                    right, left, Clip("finger_gun_fire"), Clip("finger_gun_fix"), leftMask, triggerMask);
                PrefabUtility.SaveAsPrefabAsset(root, ResourceFolder + "/PSXArms.prefab");
                AssetDatabase.SaveAssets();
                return "Built supplied PSX arms with " + suppliedClips.Length + " extracted animation clips.";
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>().First(t => t.name == name);
        private static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolder + "/A_FP_PSX_" + name + ".anim");

        private static AvatarMask Mask(GameObject root, string name, bool trigger)
        {
            var mask = new AvatarMask { name = "AM_PSX_" + name };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            Transform[] bones = root.GetComponentsInChildren<Transform>();
            mask.transformCount = bones.Length;
            for (int i = 0; i < bones.Length; i++)
            {
                string n = bones[i].name;
                mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(bones[i], root.transform));
                mask.SetTransformActive(i, trigger ? n.Contains("f_index") && n.Contains(".R") :
                    n.Contains(".L") && (n.Contains("palm") || n.Contains("f_") || n.Contains("thumb")));
            }
            return Store(mask, AnimFolder + "/" + mask.name + ".mask");
        }

        private static Material Material(string name, string texture)
        {
            string texturePath = "Assets/_Project/Art/Textures/Characters/FirstPerson/PSXArms/" + texture + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (importer.filterMode != FilterMode.Point || !importer.mipmapEnabled)
            { importer.filterMode = FilterMode.Point; importer.mipmapEnabled = true; importer.SaveAndReimport(); }
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", 0.22f);
            return Store(material, MaterialFolder + "/" + name + ".mat");
        }

        private static T Store<T>(T asset, string path) where T : Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { EditorUtility.CopySerialized(asset, existing); Object.DestroyImmediate(asset); return existing; }
            AssetDatabase.CreateAsset(asset, path); return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
