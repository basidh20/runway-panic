using RunwayPanic.Agents;
using RunwayPanic.AI;
using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RunwayPanic.AITools
{
    /// <summary>
    /// Tools > IS > Set Up Agents Test Scene: prepares the open sandbox scene for testing the shared bird plumbing (F2)
    /// before the bird prefabs carry the AI components (F5). Adds, only where missing: a FlightGrid, the gameplay
    /// player (with all four guns, so switching can be tested), the GameManager, a test hawk with
    /// SimpleBirdMotor + BirdHealth + TestChaseBrain + BirdStateLabel + BlendShapeBirdAnimator, and an AgentsDebugLogger.
    /// Changes only the open scene (undoable); prefabs and assets are never modified. Do not run it on Main.unity.
    /// </summary>
    public static class AgentsTestSceneSetup
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab";
        private const string GameManagerPrefabPath = "Assets/_Project/Prefabs/Core/P_GameManager.prefab";
        private const string HawkPrefabPath = "Assets/_Project/Prefabs/Birds/P_Bird_Hawk.prefab";
        private const string HawkSpeciesPath = "Assets/_Project/ScriptableObjects/Birds/BirdSpecies_Hawk.asset";
        private const string GunPrefabPathFormat = "Assets/_Project/Prefabs/Weapons/P_Gun_Tier{0}.prefab";
        private const string TestBirdName = "TestBird_Hawk";
        private const string LoggerName = "AgentsDebugLogger";
        private const string MeshName = "Bird_Mesh";

        [MenuItem("Tools/IS/Set Up Agents Test Scene")]
        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Agents Test Scene", "Run this in Edit mode (stop Play first).", "OK");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path.EndsWith("/Main.unity"))
            {
                EditorUtility.DisplayDialog("Agents Test Scene", "Main.unity belongs to the World Builder. Open your sandbox scene first.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Set Up Agents Test Scene");
            int undoGroup = Undo.GetCurrentGroup();
            var report = new System.Text.StringBuilder("[AgentsSetup] ");

            if (Object.FindAnyObjectByType<FlightGrid>() == null)
            {
                var grid = new GameObject("FlightGrid");
                Undo.RegisterCreatedObjectUndo(grid, "Add FlightGrid");
                Undo.AddComponent<FlightGrid>(grid);
                report.Append("added FlightGrid; ");
            }

            if (Object.FindAnyObjectByType<PlayerHealth>() == null)
            {
                GameObject marker = GameObject.Find("PlayerStart");
                Vector3 position = marker != null ? marker.transform.position : new Vector3(0f, 0.12f, -24f);
                GameObject player = InstantiatePrefab(PlayerPrefabPath, position, Quaternion.identity);
                if (player != null)
                {
                    GiveAllGuns(player);
                    report.Append("added player with 4 guns; ");
                }
            }

            if (Object.FindAnyObjectByType<GameManager>() == null && InstantiatePrefab(GameManagerPrefabPath, Vector3.zero, Quaternion.identity) != null)
                report.Append("added GameManager; ");

            GameObject bird = GameObject.Find(TestBirdName);
            if (bird == null)
            {
                GameObject marker = GameObject.Find("BirdSpawn_01");
                Vector3 position = marker != null ? marker.transform.position : new Vector3(0f, 8f, 0f);
                bird = InstantiatePrefab(HawkPrefabPath, position, Quaternion.identity);
                if (bird != null)
                {
                    bird.name = TestBirdName;
                    AddBirdComponents(bird);
                    report.Append($"added {TestBirdName} at {position}; ");
                }
            }

            if (Object.FindAnyObjectByType<AgentsDebugLogger>() == null)
            {
                var logger = new GameObject(LoggerName);
                Undo.RegisterCreatedObjectUndo(logger, "Add AgentsDebugLogger");
                Undo.AddComponent<AgentsDebugLogger>(logger);
                report.Append("added AgentsDebugLogger; ");
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            if (bird != null)
                Selection.activeGameObject = bird;

            Debug.Log(report.Length > 15 ? report.ToString() + "save the scene, then press Play." : "[AgentsSetup] scene already set up; nothing added.");
        }

        private static GameObject InstantiatePrefab(string path, Vector3 position, Quaternion rotation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[AgentsSetup] prefab not found: {path}");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(instance, "Add " + prefab.name);
            return instance;
        }

        // Scene override on this player only: own all four guns so 1/2 switches (and the slow sniper) can be tested.
        private static void GiveAllGuns(GameObject player)
        {
            var switcher = player.GetComponentInChildren<RunwayPanic.Weapons.WeaponSwitcher>();
            if (switcher == null)
                return;

            var serialized = new SerializedObject(switcher);
            SerializedProperty owned = serialized.FindProperty("ownedWeapons");
            if (owned == null)
                return;

            owned.arraySize = 4;
            for (int tier = 1; tier <= 4; tier++)
                owned.GetArrayElementAtIndex(tier - 1).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(string.Format(GunPrefabPathFormat, tier));
            serialized.ApplyModifiedProperties();
        }

        // Adds only what the prefab doesn't already have (after F5 the prefab carries everything except the brain).
        private static void AddBirdComponents(GameObject bird)
        {
            if (bird.GetComponent<IBirdMotor>() == null)
                Undo.AddComponent<SimpleBirdMotor>(bird);
            BirdHealth health = bird.GetComponent<BirdHealth>();
            if (health == null)
                health = Undo.AddComponent<BirdHealth>(bird);
            if (bird.GetComponent<BirdBrain>() == null)
                Undo.AddComponent<TestChaseBrain>(bird);
            if (bird.GetComponent<BirdStateLabel>() == null)
                Undo.AddComponent<BirdStateLabel>(bird);

            var serialized = new SerializedObject(health);
            SerializedProperty species = serialized.FindProperty("species");
            if (species.objectReferenceValue == null)
            {
                species.objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableObject>(HawkSpeciesPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (bird.GetComponentInChildren<IBirdAnimator>() != null)
                return;

            // The animator goes on the model child (the object holding Bird_Mesh, directly under the root).
            Transform model = null;
            foreach (SkinnedMeshRenderer mesh in bird.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mesh.name != MeshName)
                    continue;
                model = mesh.transform;
                while (model.parent != null && model.parent != bird.transform)
                    model = model.parent;
                break;
            }

            Undo.AddComponent<BlendShapeBirdAnimator>(model != null ? model.gameObject : bird);
        }
    }
}
