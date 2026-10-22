using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    public static class HangarInteriorLighting
    {
        private const string Root = "Environment/Buildings/Hangar_Greybox/P_Greybox_Hangar";
        private const string BulbPath = "Assets/_Project/Art/Materials/WorldSurfaces/M_Hangar_Bulb_Emissive.mat";

        [MenuItem("Tools/Runway Panic/Improve Hangar Interior Lighting")]
        private static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var root = GameObject.Find(Root);
            if (root == null) throw new InvalidOperationException("Main hangar is missing.");
            var lamps = root.GetComponentsInChildren<Light>(true)
                .Where(l => l.name == "Interior_Preview_Light" && l.type == LightType.Point).ToArray();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Greybox/M_Greybox_Metal.mat");
            var cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            if (lamps.Length != 2 || shader == null || metal == null || cylinder == null ||
                lamps.Any(l => l.transform.Find("Ceiling_Bulb_Fixture/Bulb") == null))
                throw new InvalidOperationException("Expected two existing lamps with visible bulb fixtures.");
            var bulbMaterial = AssetDatabase.LoadAssetAtPath<Material>(BulbPath);
            if (bulbMaterial == null)
            {
                bulbMaterial = new Material(shader) { name = "M_Hangar_Bulb_Emissive" };
                bulbMaterial.SetColor("_BaseColor", new Color(.8f, .74f, .61f));
                bulbMaterial.SetColor("_EmissionColor", new Color(6f, 5.3f, 4.1f));
                bulbMaterial.SetFloat("_Smoothness", .25f);
                AssetDatabase.CreateAsset(bulbMaterial, BulbPath);
            }
            Undo.RecordObject(bulbMaterial, "Enable HDR bulb emission");
            // URP's material validation removes _EMISSION when no emissive GI flag is set.
            // This flag keeps the visual emission enabled; no lighting bake is started here.
            bulbMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            bulbMaterial.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(bulbMaterial);
            Undo.SetCurrentGroupName("Improve hangar interior lighting");
            foreach (var lamp in lamps)
            {
                Undo.RecordObject(lamp, "Soften ceiling fill light");
                lamp.color = new Color(1f, .93f, .83f);
                lamp.intensity = 1.1f;
                lamp.range = 11f;
                lamp.shadows = LightShadows.None;
                lamp.lightmapBakeType = LightmapBakeType.Realtime;
                PrefabUtility.RecordPrefabInstancePropertyModifications(lamp);
                var fixture = lamp.transform.Find("Ceiling_Bulb_Fixture");
                var bulb = fixture.Find("Bulb");
                var renderer = bulb.GetComponent<MeshRenderer>();
                Undo.RecordObjects(new UnityEngine.Object[] { bulb, renderer }, "Improve visible hangar bulb");
                bulb.localScale = new Vector3(.28f, .34f, .28f);
                renderer.sharedMaterial = bulbMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(bulb);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                Part(fixture, "Lamp_Socket", new Vector3(0, .18f, 0), new Vector3(.13f, .09f, .13f));
                Part(fixture, "Lamp_Shade", new Vector3(0, .30f, 0), new Vector3(.62f, .045f, .62f));

                var down = lamp.transform.Find("Downlight");
                if (down == null)
                {
                    var go = new GameObject("Downlight");
                    Undo.RegisterCreatedObjectUndo(go, "Add hangar downlight");
                    down = go.transform;
                    down.SetParent(lamp.transform, false);
                }
                Undo.RecordObject(down, "Aim hangar downlight");
                down.localPosition = new Vector3(0, -.20f, 0);
                down.localRotation = Quaternion.Euler(90, 0, 0);
                var light = down.GetComponent<Light>();
                if (light == null) light = Undo.AddComponent<Light>(down.gameObject);
                Undo.RecordObject(light, "Configure hangar downlight");
                light.type = LightType.Spot;
                light.color = lamp.color;
                light.intensity = 45f;
                light.range = 18f;
                light.spotAngle = 120f;
                light.innerSpotAngle = 90f;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.shadows = LightShadows.Hard;
                light.shadowStrength = .65f;
                light.shadowBias = .04f;
                light.shadowNormalBias = .12f;
                light.shadowNearPlane = .1f;
                light.shadowCustomResolution = 512;
                var data = down.GetComponent<UniversalAdditionalLightData>();
                if (data == null) data = Undo.AddComponent<UniversalAdditionalLightData>(down.gameObject);
                Undo.RecordObject(data, "Limit hangar shadow cost");
                data.usePipelineSettings = false;
                // URP only exposes this setter during Play; configure the saved Editor property.
                var serialized = new SerializedObject(data);
                serialized.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierCustom;
                serialized.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(down);
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main hangar lighting.");
            Debug.Log("[HangarLighting] Saved two gentle bulb fills, two broad downward spotlights with 512px hard shadows, and dedicated HDR-emissive bulbs. No bake or global lighting/profile changes.");

            void Part(Transform fixture, string name, Vector3 position, Vector3 scale)
            {
                var part = fixture.Find(name);
                if (part == null)
                {
                    var go = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(go, "Add lamp housing");
                    part = go.transform;
                    part.SetParent(fixture, false);
                    go.AddComponent<MeshFilter>().sharedMesh = cylinder;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = metal;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }
                Undo.RecordObject(part, "Size lamp housing");
                part.localPosition = position;
                part.localScale = scale;
            }
        }

        [MenuItem("Tools/Runway Panic/Preview Improved Hangar Lighting")]
        private static void Preview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
            var root = GameObject.Find(Root).transform;
            TerminalFlowerDressing.CaptureView(root.TransformPoint(new Vector3(10f, 1.7f, 0)),
                root.TransformPoint(new Vector3(-2, 2.6f, 0)), "/private/tmp/RunwayPanic-HangarLighting.png");
            TerminalFlowerDressing.CaptureView(root.TransformPoint(new Vector3(4, 1.7f, -5)),
                root.TransformPoint(new Vector3(0, 4.9f, -7)), "/private/tmp/RunwayPanic-HangarBulb.png");
            var spots = root.GetComponentsInChildren<Light>().Where(l => l.name == "Downlight").ToArray();
            var shadows = spots.Select(l => l.shadows).ToArray();
            try
            {
                foreach (var light in spots) light.shadows = LightShadows.None;
                TerminalFlowerDressing.CaptureView(root.TransformPoint(new Vector3(10f, 1.7f, 0)),
                    root.TransformPoint(new Vector3(-2, 2.6f, 0)), "/private/tmp/RunwayPanic-HangarLighting-NoLampShadows.png");
            }
            finally { for (int i = 0; i < spots.Length; i++) spots[i].shadows = shadows[i]; }
        }
    }
}
