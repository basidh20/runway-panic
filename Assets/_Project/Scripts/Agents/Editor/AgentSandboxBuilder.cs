using System.IO;
using RunwayPanic.Agents.Testing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.EditorTools
{
    /// <summary>
    /// Tools > Agents > Create Agent Sandbox: builds Scenes/Sandbox/Sandbox_Chamika.unity, the Agent Controller's test
    /// scene: an 80 m ground, a light, a 1.8 m player-sized capsule for scale, one bird of each species flying a
    /// TestPathFeeder route, a camera that can chase any bird (keys 1-4, 0 = overview) and a disabled StressTest object.
    /// If the scene already exists it is only opened, never overwritten (delete it to rebuild).
    /// Run Build Bird Animations and Setup Bird Prefabs first.
    /// </summary>
    public static class AgentSandboxBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Sandbox/Sandbox_Chamika.unity";
        const string PrefabFolder = "Assets/_Project/Prefabs/Birds/";
        static readonly string[] Species = { "Gull", "Pigeon", "Crow", "Hawk" };

        [MenuItem("Tools/Agents/Create Agent Sandbox")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                Debug.Log($"[Agents] {ScenePath} already exists - opened it. Delete it first to rebuild.");
                return;
            }

            if (LoadPrefabs() == null)
                return; // checked before the open scene is replaced

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Opening a scene unloads every asset nothing in it references, which destroys prefab references loaded
            // before the switch. Load them again now.
            BirdMotor[] prefabs = LoadPrefabs();

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground_80m";
            ground.transform.localScale = new Vector3(8f, 1f, 8f); // Unity's plane is 10 m

            GameObject scale = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            scale.name = "PlayerScale_1.8m";
            scale.transform.position = new Vector3(0f, 0.9f, -4f);
            scale.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f); // Unity's capsule is 2 m tall

            var birds = new Transform[Species.Length];
            // Feeder position and zigzag size per species; the hawk flies a big right-angled route of child corners.
            CreateBird(prefabs[0], "Gull", new Vector3(0f, 6f, 8f), 4, 4, birds, 0);
            CreateBird(prefabs[1], "Pigeon", new Vector3(-10f, 4f, -6f), 3, 3, birds, 1);
            CreateBird(prefabs[2], "Crow", new Vector3(10f, 8f, -6f), 3, 3, birds, 2);
            GameObject hawkFeeder = CreateBird(prefabs[3], "Hawk", Vector3.zero, 4, 4, birds, 3);
            AddCorners(hawkFeeder.transform, 11f,
                new Vector2(-18f, -14f), new Vector2(18f, -14f), new Vector2(18f, 0f),
                new Vector2(6f, 0f), new Vector2(6f, 14f), new Vector2(-18f, 14f));

            CreateCamera(birds);
            CreateStressTest(prefabs);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Agents] Created {ScenePath}. Press Play: keys 1-4 chase a bird, 0 = overview, H/K/R = damage/kill/respawn.");
        }

        /// <summary>
        /// Tools > Agents > Run Automated Check: opens Sandbox_Chamika and presses Play; <see cref="AgentAutoCheck"/>
        /// then takes the flight screenshots, runs the three stress-test modes and leaves Play mode by itself.
        /// </summary>
        [MenuItem("Tools/Agents/Run Automated Check")]
        public static void RunAutomatedCheck()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Agents] Stop Play mode first, then run the automated check.");
                return;
            }
            if (!File.Exists(ScenePath))
            {
                Debug.LogWarning("[Agents] Sandbox_Chamika not found - run Tools > Agents > Create Agent Sandbox first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(ScenePath); // the saved version, so earlier test toggles are gone
            SessionState.SetBool(AgentAutoCheck.SessionKey, true);
            EditorApplication.isPlaying = true;
            Debug.Log("[Agents] Automated check started: about 2 minutes, Play mode stops by itself.");
        }

        /// <summary>The BirdMotor of each species prefab, or null (with a warning) if one is not set up yet.</summary>
        static BirdMotor[] LoadPrefabs()
        {
            var prefabs = new BirdMotor[Species.Length];
            for (int i = 0; i < Species.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}P_Bird_{Species[i]}.prefab");
                prefabs[i] = prefab != null ? prefab.GetComponent<BirdMotor>() : null;
                if (prefabs[i] == null)
                {
                    Debug.LogWarning($"[Agents] P_Bird_{Species[i]} has no BirdMotor - run Tools > Agents > Setup Bird Prefabs first.");
                    return null;
                }
            }
            return prefabs;
        }

        static GameObject CreateBird(BirdMotor prefab, string species, Vector3 feederPosition, int legs, int legCells,
            Transform[] birds, int index)
        {
            var bird = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
            bird.name = $"Bird_{species}";
            bird.transform.position = feederPosition;
            birds[index] = bird.transform;

            // Sandbox only: hide dead birds instead of destroying them, so R can bring them back.
            using (var health = new SerializedObject(bird.GetComponent<BirdHealth>()))
            {
                health.FindProperty("cleanup").intValue = (int)BirdHealth.DeathCleanup.Deactivate;
                health.ApplyModifiedPropertiesWithoutUndo();
            }

            var feeder = new GameObject($"Feeder_{species}");
            feeder.transform.position = feederPosition;
            using (var serialized = new SerializedObject(feeder.AddComponent<TestPathFeeder>()))
            {
                serialized.FindProperty("bird").objectReferenceValue = bird.GetComponent<BirdMotor>();
                serialized.FindProperty("zigzagLegs").intValue = legs;
                serialized.FindProperty("legCells").intValue = legCells;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return feeder;
        }

        /// <summary>Route corners as child Transforms (drag them around in the Scene view to change the route).</summary>
        static void AddCorners(Transform feeder, float height, params Vector2[] corners)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                var corner = new GameObject($"Corner_{i:00}").transform;
                corner.SetParent(feeder, false);
                corner.position = new Vector3(corners[i].x, height, corners[i].y);
            }
        }

        static void CreateCamera(Transform[] birds)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 14f, -34f);
            cameraObject.transform.LookAt(new Vector3(0f, 6f, 2f));
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();

            using (var serialized = new SerializedObject(cameraObject.AddComponent<SandboxCameraRig>()))
            {
                SerializedProperty list = serialized.FindProperty("birds");
                list.arraySize = birds.Length;
                for (int i = 0; i < birds.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = birds[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void CreateStressTest(BirdMotor[] prefabs)
        {
            var stress = new GameObject("StressTest");
            using (var serialized = new SerializedObject(stress.AddComponent<BirdStressTest>()))
            {
                SerializedProperty list = serialized.FindProperty("prefabs");
                list.arraySize = prefabs.Length;
                for (int i = 0; i < prefabs.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            stress.SetActive(false); // enable it (and disable the four test birds) to run the stress test
        }
    }
}
