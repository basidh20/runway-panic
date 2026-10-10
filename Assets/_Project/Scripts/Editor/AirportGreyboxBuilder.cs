using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using RunwayPanic.Greybox;

namespace RunwayPanic.EditorTools
{
    /// <summary>Builds ordinary editable scene objects and prefabs; no runtime level generation.</summary>
    public static class AirportGreyboxBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Greybox";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Environment/Greybox";
        private static Material ground, pavement, concrete, metal, paint, accent, glazing;

        [MenuItem("Tools/Runway Panic/Build Airport Greybox")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Main.unity already exists. This builder will not overwrite it.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            ground = Material("M_Greybox_Ground", new Color(0.29f, 0.34f, 0.33f));
            pavement = Material("M_Greybox_Runway", new Color(0.16f, 0.19f, 0.21f));
            concrete = Material("M_Greybox_Concrete", new Color(0.61f, 0.65f, 0.66f));
            metal = Material("M_Greybox_Metal", new Color(0.29f, 0.34f, 0.38f));
            paint = Material("M_Greybox_Marking", new Color(0.91f, 0.93f, 0.89f));
            accent = Material("M_Greybox_Safety", new Color(0.89f, 0.57f, 0.20f));
            glazing = Material("M_Greybox_Landmark", new Color(0.29f, 0.51f, 0.56f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var environment = Group("Environment");
            var runway = Group("Runway", environment);
            var buildings = Group("Buildings", environment);
            var cover = Group("Cover", environment);
            var background = Group("Background", environment);
            var boundaries = Group("Boundaries", environment);
            var markers = Group("GameplayMarkers");
            var lighting = Group("Lighting");

            Cube("Ground_100x80", environment, new Vector3(0, -0.5f, 0), new Vector3(100, 1, 80), ground);
            Cube("Runway_60x14", runway, new Vector3(0, 0.018f, 0), new Vector3(14, 0.036f, 60), pavement, false);
            Cube("Apron_A", runway, new Vector3(-17, 0.016f, 12), new Vector3(20, 0.032f, 14), concrete, false);
            Cube("Apron_B", runway, new Vector3(17, 0.016f, -12), new Vector3(20, 0.032f, 14), concrete, false);
            for (int i = -4; i <= 4; i++)
                Cube("CentreStripe_" + (i + 5).ToString("00"), runway, new Vector3(0, 0.044f, i * 6), new Vector3(0.28f, 0.012f, 3), paint, false);
            foreach (float x in new[] { -6.45f, 6.45f })
                Cube("RunwayEdge_" + x, runway, new Vector3(x, 0.044f, 0), new Vector3(0.15f, 0.012f, 58), paint, false);
            for (int i = 0; i < 6; i++)
                foreach (float z in new[] { -27f, 27f })
                    Cube("Threshold_" + z + "_" + i, runway, new Vector3(-4.5f + i * 1.8f, 0.044f, z), new Vector3(0.8f, 0.012f, 2.5f), paint, false);
            Label("RunwayNumber_09", "09", runway, new Vector3(0, 0.052f, -22), new Vector3(90, 0, 0), 0.55f, paint.color);
            Label("RunwayNumber_27", "27", runway, new Vector3(0, 0.052f, 22), new Vector3(90, 180, 0), 0.55f, paint.color);

            var shelterPrefab = MakeShelter();
            PlacePrefab(shelterPrefab, "Shelter_A", buildings, new Vector3(-21, 0, 12), -90);
            PlacePrefab(shelterPrefab, "Shelter_B", buildings, new Vector3(21, 0, -12), 90);
            GroundLabel("Shelter_A_Label", "SHELTER A", environment, new Vector3(-13.8f, 0.051f, 12), -90, 0.19f);
            GroundLabel("Shelter_B_Label", "SHELTER B", environment, new Vector3(13.8f, 0.051f, -12), 90, 0.19f);

            var barricadePrefab = MakeBarricade();
            Vector3[] positions = { new Vector3(-10, 0, -18), new Vector3(10, 0, -21), new Vector3(-10, 0, 23), new Vector3(10, 0, 17), new Vector3(-13, 0, -2), new Vector3(13, 0, 3) };
            for (int i = 0; i < positions.Length; i++)
                PlacePrefab(barricadePrefab, "Cover_" + (i + 1).ToString("00"), cover, positions[i], i % 2 == 0 ? 12 : -18);

            Cube("Boundary_West", boundaries, new Vector3(-49.75f, 1.3f, 0), new Vector3(0.5f, 2.6f, 80), metal);
            Cube("Boundary_East", boundaries, new Vector3(49.75f, 1.3f, 0), new Vector3(0.5f, 2.6f, 80), metal);
            Cube("Boundary_North", boundaries, new Vector3(0, 1.3f, 39.75f), new Vector3(100, 2.6f, 0.5f), metal);
            Cube("Boundary_South", boundaries, new Vector3(0, 1.3f, -39.75f), new Vector3(100, 2.6f, 0.5f), metal);

            var tower = Group("ControlTower_Placeholder", background);
            Cube("TowerBase", tower, new Vector3(-33, 3, 28), new Vector3(5, 6, 5), concrete);
            Cube("TowerShaft", tower, new Vector3(-33, 9, 28), new Vector3(3, 6, 3), concrete);
            Cube("TowerCab", tower, new Vector3(-33, 13, 28), new Vector3(7, 2, 7), glazing);
            Cube("TowerRoof", tower, new Vector3(-33, 14.3f, 28), new Vector3(8, 0.6f, 8), metal);
            Cube("Terminal_Placeholder", background, new Vector3(20, 3, 30), new Vector3(20, 6, 8), concrete);
            Cube("TerminalRoof", background, new Vector3(20, 6.2f, 30), new Vector3(21, 0.4f, 9), metal);
            for (int i = 0; i < 5; i++) Cube("TerminalWindow_" + i, background, new Vector3(12 + i * 4, 3.2f, 25.96f), new Vector3(2.5f, 2, 0.08f), glazing, false);

            Marker("PlayerStart", markers, new Vector3(0, 0.12f, -24));
            var birdMarkers = Group("BirdSpawnPoints", markers);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Marker("BirdSpawn_" + (i + 1).ToString("00"), birdMarkers, new Vector3(Mathf.Cos(angle) * 40, 6, Mathf.Sin(angle) * 31));
            }
            var pickups = Group("PickupPoints", markers);
            foreach (Vector3 p in new[] { new Vector3(-21, 0.2f, 12), new Vector3(21, 0.2f, -12), new Vector3(-12, 0.2f, -20), new Vector3(12, 0.2f, 20) })
                Marker("Pickup_" + pickups.childCount.ToString("00"), pickups, p);
            var checkpoints = Group("Checkpoints", markers);
            Marker("Checkpoint_A", checkpoints, new Vector3(-13, 0.12f, 12));
            Marker("Checkpoint_B", checkpoints, new Vector3(13, 0.12f, -12));
            Marker("ShopPosition_Reserved", markers, new Vector3(-30, 0.12f, -26));

            var sun = Group("Sun_Directional", lighting).gameObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.color = new Color(1, 0.96f, 0.89f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.64f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.47f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.28f, 0.29f);
            RenderSettings.fog = false;

            var preview = Group("GreyboxPreview_TEMPORARY");
            var overview = Group("Camera_Overview", preview).gameObject.AddComponent<Camera>();
            overview.tag = "MainCamera";
            overview.transform.position = new Vector3(66, 67, -70);
            overview.transform.LookAt(new Vector3(0, 1, 0));
            overview.orthographic = true;
            overview.orthographicSize = 51;
            overview.farClipPlane = 400;
            overview.clearFlags = CameraClearFlags.SolidColor;
            overview.backgroundColor = new Color(0.13f, 0.17f, 0.20f);
            overview.gameObject.AddComponent<AudioListener>();

            var player = Group("Player_Preview", preview).gameObject;
            player.transform.position = new Vector3(0, 0.12f, -24);
            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0, 0.9f, 0);
            controller.stepOffset = 0.25f;
            controller.skinWidth = 0.035f;
            var walk = Group("Camera_Walk", player.transform).gameObject.AddComponent<Camera>();
            walk.transform.localPosition = new Vector3(0, 1.65f, 0);
            walk.fieldOfView = 75;
            walk.nearClipPlane = 0.05f;
            walk.farClipPlane = 300;
            walk.enabled = false;
            walk.gameObject.AddComponent<AudioListener>().enabled = false;
            var previewController = player.AddComponent<GreyboxWalkthrough>();
            previewController.walkCamera = walk;
            previewController.overviewCamera = overview;

            EditorSceneManager.SaveScene(scene, ScenePath);
            IncludeMainInBuild();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = environment.gameObject;
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null) sceneView.LookAt(new Vector3(0, 0, 0), Quaternion.Euler(50, -40, 0), 90, false, true);
            Validate();
            Debug.Log("Airport greybox saved to " + ScenePath + ". Press Play, then Tab to walk. No NavMesh or lighting bake is included yet.");
        }

        private static GameObject MakeShelter()
        {
            var root = Group("P_Greybox_Shelter");
            Cube("Floor", root, new Vector3(0, 0.025f, 0), new Vector3(10, 0.05f, 8), concrete);
            Cube("Wall_Back", root, new Vector3(0, 2, 3.8f), new Vector3(10, 4, 0.4f), concrete);
            Cube("Wall_Left", root, new Vector3(-4.8f, 2, 0), new Vector3(0.4f, 4, 7.6f), concrete);
            Cube("Wall_Right", root, new Vector3(4.8f, 2, 0), new Vector3(0.4f, 4, 7.6f), concrete);
            Cube("Front_Left", root, new Vector3(-3.2f, 2, -3.8f), new Vector3(3.6f, 4, 0.4f), concrete);
            Cube("Front_Right", root, new Vector3(3.2f, 2, -3.8f), new Vector3(3.6f, 4, 0.4f), concrete);
            Cube("Door_Lintel", root, new Vector3(0, 3.5f, -3.8f), new Vector3(2.8f, 1, 0.4f), concrete);
            Cube("Roof", root, new Vector3(0, 4.15f, 0), new Vector3(10.5f, 0.3f, 8.5f), metal);
            Cube("EntranceCanopy", root, new Vector3(0, 3.04f, -4.75f), new Vector3(3.6f, 0.15f, 2), accent);
            Cube("EntryStripe", root, new Vector3(0, 0.056f, -3.65f), new Vector3(2.5f, 0.01f, 0.2f), accent, false);
            Label("ShelterSign", "SHELTER", root, new Vector3(0, 3.5f, -4.015f), Vector3.zero, 0.085f, paint.color);
            var interiorLight = Group("InteriorPreviewLight", root).gameObject.AddComponent<Light>();
            interiorLight.transform.localPosition = new Vector3(0, 3.4f, 0);
            interiorLight.type = LightType.Point;
            interiorLight.range = 10;
            interiorLight.intensity = 2;
            interiorLight.shadows = LightShadows.None;
            return SavePrefab(root, "P_Greybox_Shelter");
        }

        private static GameObject MakeBarricade()
        {
            var root = Group("P_Greybox_Barricade");
            Cube("Base", root, new Vector3(0, 0.15f, 0), new Vector3(3.6f, 0.3f, 1.2f), concrete);
            Cube("CoverBody", root, new Vector3(0, 0.8f, 0), new Vector3(3.2f, 1.0f, 0.7f), accent);
            Cube("Top", root, new Vector3(0, 1.35f, 0), new Vector3(3.3f, 0.1f, 0.8f), metal);
            return SavePrefab(root, "P_Greybox_Barricade");
        }

        private static GameObject SavePrefab(Transform root, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabFolder + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return prefab;
        }

        private static void PlacePrefab(GameObject prefab, string name, Transform parent, Vector3 position, float yaw)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        private static Material Material(string name, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader not found.");
            material = new Material(shader) { name = name, color = color };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(material, MaterialFolder + "/" + name + ".mat");
            return material;
        }

        private static Transform Group(string name, Transform parent = null)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            return obj.transform;
        }

        private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision = true)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }

        private static void Marker(string name, Transform parent, Vector3 position)
        {
            Group(name, parent).position = position;
        }

        private static void GroundLabel(string name, string text, Transform parent, Vector3 position, float yaw, float size)
        {
            Label(name, text, parent, position, new Vector3(90, yaw, 0), size, paint.color);
        }

        private static void Label(string name, string text, Transform parent, Vector3 position, Vector3 rotation, float size, Color color)
        {
            var obj = Group(name, parent).gameObject;
            obj.transform.localPosition = position;
            obj.transform.localRotation = Quaternion.Euler(rotation);
            var label = obj.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = size;
            label.color = color;
        }

        private static void IncludeMainInBuild()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var other in EditorBuildSettings.scenes)
                if (other.path != ScenePath) scenes.Add(other);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [MenuItem("Tools/Runway Panic/Refresh Greybox Presentation")]
        public static void RefreshPresentation()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before refreshing.");
            string path = PrefabFolder + "/P_Greybox_Shelter.prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sign = contents.transform.Find("ShelterSign");
                sign.localRotation = Quaternion.identity;
                sign.GetComponent<TextMesh>().characterSize = 0.085f;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            IncludeMainInBuild();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("Greybox shelter signs updated; Main is the first build scene.");
        }

        [MenuItem("Tools/Runway Panic/Validate Airport Greybox")]
        public static void Validate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Validate the saved level outside Play mode.");
            if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open Main.unity before validating.");
            Physics.SyncTransforms();
            var report = new List<string> { "Airport greybox validation", "Scene: " + ScenePath, "Unity: " + Application.unityVersion, "" };
            int failed = 0;
            Action<bool, string> check = (ok, message) => { report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failed++; };
            check(GameObject.Find("Ground_100x80").GetComponent<BoxCollider>() != null, "Ground has solid box collision");
            check(GameObject.Find("BirdSpawnPoints").transform.childCount == 8, "Eight bird spawn markers exist");
            check(GameObject.Find("Cover").transform.childCount == 6, "Six reusable cover instances exist");
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
                check(renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.name != "Hidden/InternalErrorShader", "Valid material: " + renderer.name);

            foreach (float x in new[] { -45f, -10f, 0f, 10f, 45f })
                foreach (float z in new[] { -35f, -20f, 0f, 20f, 35f })
                    check(Physics.Raycast(new Vector3(x, 0.5f, z), Vector3.down, 1), "Ground supports point " + x + ", " + z);

            // Collision smoke tests use an actual human-sized CharacterController, not just raycasts.
            var test = new GameObject("ValidationController_TEMP");
            var cc = test.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.25f; cc.skinWidth = 0.035f;
            try
            {
                Action<Vector3> reset = pos => { cc.enabled = false; test.transform.position = pos; cc.enabled = true; Physics.SyncTransforms(); };
                Action<Vector3, int> move = (delta, frames) => { for (int i = 0; i < frames; i++) cc.Move(delta); };
                reset(new Vector3(-13, 0.08f, 12));
                move(new Vector3(-0.1f, -0.02f, 0), 80);
                check(test.transform.position.x < -20.5f, "Character passes through Shelter A doorway into interior");
                move(new Vector3(0.1f, -0.02f, 0), 80);
                check(test.transform.position.x > -13.5f, "Character exits Shelter A doorway");
                reset(new Vector3(13, 0.08f, -12));
                move(new Vector3(0.1f, -0.02f, 0), 80);
                check(test.transform.position.x > 20.5f, "Character passes through Shelter B doorway into interior");
                move(new Vector3(-0.1f, -0.02f, 0), 80);
                check(test.transform.position.x < 13.5f, "Character exits Shelter B doorway");
                reset(new Vector3(-21, 0.08f, 12));
                move(new Vector3(0, -0.02f, 0.1f), 80);
                check(test.transform.position.z < 16.5f && test.transform.position.z > 15, "Shelter side wall blocks character");
                reset(new Vector3(0, 0.08f, 38));
                move(new Vector3(0, -0.02f, 0.1f), 50);
                check(test.transform.position.z < 39.5f, "Perimeter wall contains character");
                reset(new Vector3(0, 2, 0));
                move(new Vector3(0, -0.1f, 0), 50);
                check(test.transform.position.y > -0.1f && test.transform.position.y < 0.2f, "Character lands on ground without falling through");
            }
            finally { UnityEngine.Object.DestroyImmediate(test); }
            report.Add(""); report.Add("Failures: " + failed);
            Directory.CreateDirectory("Docs/WorldBuilder");
            File.WriteAllLines("Docs/WorldBuilder/GreyboxValidation.txt", report);
            if (failed > 0) throw new InvalidOperationException("Greybox validation failed: " + failed + ". See Docs/WorldBuilder/GreyboxValidation.txt.");
            Debug.Log("Airport greybox validation passed. Report: Docs/WorldBuilder/GreyboxValidation.txt");
        }
    }
}
