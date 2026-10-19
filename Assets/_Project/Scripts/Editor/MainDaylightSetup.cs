using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Adjusts outdoor daylight without changing the sun's direction or gameplay.</summary>
    public static class MainDaylightSetup
    {
        [MenuItem("Tools/Runway Panic/Prepare Fixed Buildings for Lighting Bake")]
        private static void PrepareFixedBuildingsForBake()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");

            var shelterParts = new[] { "Floor", "Roof", "Wall_Back", "Wall_Left", "Wall_Right",
                "Front_Left", "Front_Right", "Door_Lintel", "EntranceCanopy" };
            var hangarParts = new[] { "Floor", "Roof", "Wall_South", "Wall_West", "Front_Jamb_North",
                "Front_Jamb_South", "Front_Lintel", "Side_Exit_Jamb_West", "Side_Exit_Jamb_East", "Side_Exit_Lintel" };
            var paths = shelterParts.SelectMany(part => new[] {
                "Environment/Buildings/Shelter_A/" + part, "Environment/Buildings/Shelter_B/" + part })
                .Concat(hangarParts.Select(part => "Environment/Buildings/Hangar_Greybox/P_Greybox_Hangar/" + part));
            var surfaces = paths.Select(path => GameObject.Find(path) ??
                throw new InvalidOperationException("Missing fixed building surface: " + path)).ToArray();
            foreach (var surface in surfaces)
            {
                var filter = surface.GetComponent<MeshFilter>();
                if (surface.GetComponent<MeshRenderer>() == null || filter == null || filter.sharedMesh == null ||
                    !filter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                    throw new InvalidOperationException("Missing lightmap UVs or renderer: " + surface.name);
            }

            foreach (var surface in surfaces)
            {
                Undo.RecordObject(surface, "Prepare fixed building surface for baked lighting");
                GameObjectUtility.SetStaticEditorFlags(surface,
                    GameObjectUtility.GetStaticEditorFlags(surface) | StaticEditorFlags.ContributeGI);
                PrefabUtility.RecordPrefabInstancePropertyModifications(surface);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save Main after setting building lighting flags.");
            Debug.Log($"[AirportC4] Prepared {surfaces.Length} fixed shelter and hangar surfaces; no lighting was baked.");
        }

        [MenuItem("Tools/Runway Panic/Prepare Ground and Runway for Lighting Bake")]
        private static void PrepareGroundAndRunwayForBake()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");

            var paths = new[] { "Environment/Ground_100x80", "Environment/Runway/Runway_75x14" };
            var surfaces = paths.Select(GameObject.Find).ToArray();
            foreach (var surface in surfaces)
            {
                var filter = surface != null ? surface.GetComponent<MeshFilter>() : null;
                if (surface == null || surface.GetComponent<MeshRenderer>() == null || filter == null ||
                    filter.sharedMesh == null || !filter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                    throw new InvalidOperationException("Expected ground/runway mesh with lightmap UVs is missing.");
            }

            foreach (var surface in surfaces)
            {
                Undo.RecordObject(surface, "Prepare airport surface for baked lighting");
                GameObjectUtility.SetStaticEditorFlags(surface,
                    GameObjectUtility.GetStaticEditorFlags(surface) | StaticEditorFlags.ContributeGI);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save Main after setting lighting flags.");
            Debug.Log("[AirportC4] Ground and runway prepared for a later lighting bake; no lighting was baked.");
        }

        [MenuItem("Tools/Runway Panic/Audit Main Lighting Bake Readiness")]
        private static void AuditBakeReadiness()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var environment = GameObject.Find("Environment");
            if (environment == null) throw new InvalidOperationException("Main environment is missing.");

            var lines = new List<string> { "Main lighting bake readiness", "Unity: " + Application.unityVersion,
                "Read-only audit; UV2 presence does not establish a valid non-overlapping lightmap unwrap.", "" };
            var renderers = environment.GetComponentsInChildren<MeshRenderer>(false).Where(r => r.enabled).ToArray();
            int staticCount = renderers.Count(r => (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) != 0);
            lines.Add($"Active environment mesh renderers: {renderers.Length}; Contribute GI: {staticCount}");
            foreach (var group in renderers.GroupBy(r =>
            {
                var filter = r.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) return "No MeshFilter mesh: " + r.name;
                string path = AssetDatabase.GetAssetPath(mesh);
                bool uv2 = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
                return $"{(string.IsNullOrEmpty(path) ? "built-in" : path)} | {mesh.name} | UV2 present: {uv2}";
            }).OrderBy(g => g.Key))
                lines.Add($"{group.Count()} renderer(s): {group.Key}");

            lines.Add("");
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                         .Where(l => l.gameObject.scene == scene && l.enabled))
                lines.Add($"Light: {light.name} | {light.type} | {light.lightmapBakeType} | Shadows: {light.shadows}");
            int probes = UnityEngine.Object.FindObjectsByType<LightProbeGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Count(p => p.gameObject.scene == scene && p.enabled);
            int reflections = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Count(p => p.gameObject.scene == scene && p.enabled);
            lines.Add($"Active Light Probe Groups: {probes}; Reflection Probes: {reflections}");
            lines.Add($"Lightmaps currently loaded: {LightmapSettings.lightmaps.Length}");
            const string folder = "/private/tmp/RunwayPanic-C4";
            Directory.CreateDirectory(folder);
            File.WriteAllLines(folder + "/lighting-audit.txt", lines);
            Debug.Log("[AirportC4] Read-only lighting audit written to " + folder + "/lighting-audit.txt");
        }

        [MenuItem("Tools/Runway Panic/Add Hangar Light Fixtures")]
        private static void AddHangarFixtures()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            const string prefabPath = "Assets/_Project/Prefabs/Environment/Greybox/P_Greybox_Hangar.prefab";
            var bulb = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/WorldSurfaces/M_C3_ShelterBulb.mat");
            var metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Greybox/M_Greybox_Metal.mat");
            var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            if (bulb == null || metal == null || sphere == null || cylinder == null)
                throw new InvalidOperationException("Expected fixture meshes or materials are missing.");
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var lights = Array.FindAll(root.GetComponentsInChildren<Light>(true),
                    light => light.name == "Interior_Preview_Light" && light.type == LightType.Point);
                if (lights.Length != 2) throw new InvalidOperationException("Expected two hangar interior point lights.");
                foreach (var light in lights)
                {
                    var fixture = light.transform.Find("Ceiling_Bulb_Fixture");
                    if (fixture == null)
                    {
                        fixture = new GameObject("Ceiling_Bulb_Fixture").transform;
                        fixture.SetParent(light.transform, false);
                        FixturePart(fixture, "Bulb", sphere, bulb, Vector3.zero, new Vector3(.22f, .22f, .22f));
                        FixturePart(fixture, "Cable", cylinder, metal, new Vector3(0, .45f, 0), new Vector3(.025f, .35f, .025f));
                    }
                    var visibleBulb = fixture.Find("Bulb");
                    var cable = fixture.Find("Cable");
                    if (visibleBulb == null || cable == null || visibleBulb.localPosition != Vector3.zero || fixture.localPosition != Vector3.zero)
                        throw new InvalidOperationException("A hangar bulb is not aligned with its point light.");
                    var position = light.transform.localPosition;
                    light.transform.localPosition = new Vector3(position.x, 4.9f, position.z);
                    cable.localPosition = new Vector3(0, .45f, 0);
                    cable.localScale = new Vector3(.025f, .35f, .025f);
                    light.color = new Color(1f, .88f, .72f);
                }
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                    throw new InvalidOperationException("Could not save hangar fixtures.");
                AssetDatabase.SaveAssets();
                Debug.Log("[AirportC3] Added visible fixtures at the two existing hangar lights.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/Runway Panic/Add Shelter Light Fixture")]
        private static void AddShelterFixture()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");

            const string prefabPath = "Assets/_Project/Prefabs/Environment/Greybox/P_Greybox_Shelter.prefab";
            const string materialPath = "Assets/_Project/Art/Materials/WorldSurfaces/M_C3_ShelterBulb.mat";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var lamp = root.transform.Find("InteriorPreviewLight");
                if (lamp == null || lamp.GetComponent<Light>() == null)
                    throw new InvalidOperationException("Expected shelter interior light is missing.");
                var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
                var cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                var metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Greybox/M_Greybox_Metal.mat");
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (sphere == null || cylinder == null || metal == null || shader == null)
                    throw new InvalidOperationException("Fixture meshes, material or URP shader are missing.");
                var bulb = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (bulb == null)
                {
                    bulb = new Material(shader) { name = "M_C3_ShelterBulb" };
                    AssetDatabase.CreateAsset(bulb, materialPath);
                }
                // The visible bulb stays bright independently of its own point light. The
                // point light, not the unlit material, illuminates the shelter surfaces.
                bulb.shader = shader;
                bulb.SetColor("_BaseColor", new Color(1f, .95f, .82f));
                bulb.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(bulb);
                var fixture = lamp.Find("Ceiling_Bulb_Fixture");
                if (fixture == null)
                {
                    fixture = new GameObject("Ceiling_Bulb_Fixture").transform;
                    fixture.SetParent(lamp, false);
                    FixturePart(fixture, "Bulb", sphere, bulb, Vector3.zero, new Vector3(.22f, .22f, .22f));
                    FixturePart(fixture, "Cable", cylinder, metal, new Vector3(0, .45f, 0), new Vector3(.025f, .35f, .025f));
                }
                var visibleBulb = fixture.Find("Bulb");
                var cable = fixture.Find("Cable");
                if (visibleBulb == null || cable == null || visibleBulb.localPosition != Vector3.zero || fixture.localPosition != Vector3.zero)
                    throw new InvalidOperationException("The shelter bulb is not aligned with its point light.");
                var pointLight = lamp.GetComponent<Light>();
                lamp.localPosition = new Vector3(0, 3.2f, 0);
                cable.localPosition = new Vector3(0, .45f, 0);
                cable.localScale = new Vector3(.025f, .35f, .025f);
                pointLight.color = new Color(1f, .88f, .72f);
                pointLight.intensity = 2.4f;
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                    throw new InvalidOperationException("Could not save the shelter fixture.");
                AssetDatabase.SaveAssets();
                Debug.Log("[AirportC3] Added a non-colliding bulb to the existing shelter light.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void FixturePart(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        [MenuItem("Tools/Runway Panic/Capture Interior Lighting Views")]
        private static void CaptureInteriors()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
            var shelter = GameObject.Find("Environment/Buildings/Shelter_A");
            var hangar = GameObject.Find("Environment/Buildings/Hangar_Greybox/P_Greybox_Hangar");
            if (shelter == null || hangar == null) throw new InvalidOperationException("Main interiors are missing.");
            const string folder = "/private/tmp/RunwayPanic-C3";
            Directory.CreateDirectory(folder);
            Capture(shelter.transform, new Vector3(0, 1.6f, 1.6f), new Vector3(0, 1.5f, -3.5f), folder + "/shelter.png");
            Capture(shelter.transform, new Vector3(1.2f, 1.6f, 1.8f), new Vector3(0, 3.2f, 0), folder + "/bulb.png");
            Capture(hangar.transform, new Vector3(0, 1.6f, 0), new Vector3(-5, 1.5f, -9), folder + "/hangar.png");
            Capture(hangar.transform, new Vector3(2, 1.6f, -5), new Vector3(0, 4.9f, -7), folder + "/hangar-bulb.png");
            Debug.Log("[AirportC3] Captured interior lighting previews to " + folder);
        }

        private static void Capture(Transform space, Vector3 position, Vector3 target, string path)
        {
            var go = new GameObject("C3_InteriorPreview_TEMP") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 60; camera.farClipPlane = 100;
            camera.transform.position = space.TransformPoint(position);
            camera.transform.LookAt(space.TransformPoint(target));
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var old = RenderTexture.active;
            var texture = RenderTexture.GetTemporary(960, 540, 24);
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old; camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(texture); UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [MenuItem("Tools/Runway Panic/Configure Airport Daylight")]
        private static void Configure()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");

            var sun = RenderSettings.sun;
            if (sun == null || sun.type != LightType.Directional || sun.gameObject.name != "Sun_Directional")
                throw new InvalidOperationException("The expected Main directional sun is missing.");

            Undo.RecordObject(sun, "Adjust airport daylight");
            sun.color = new Color(1f, .96f, .89f);
            sun.intensity = 1.25f;
            // Leave rotation, shadow settings, light mode and sun reference unchanged for Hawk AI.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.58f, .66f, .74f);
            RenderSettings.ambientEquatorColor = new Color(.45f, .49f, .50f);
            RenderSettings.ambientGroundColor = new Color(.28f, .30f, .29f);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main scene.");
            Debug.Log("[AirportC3] Saved outdoor daylight; sun direction and shadows unchanged.");
        }
    }
}
