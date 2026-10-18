using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Core;
using RunwayPanic.Player;
using RunwayPanic.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RunwayPanic.EditorTools
{
    /// <summary>
    /// Installs S2's existing prefabs in Main without rebuilding them or the airport.
    /// Editor-only, explicit and repeatable; never runs automatically on import or Play.
    /// </summary>
    public static class MainPlayerIntegration
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        public const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab";
        public const string ManagerPrefabPath = "Assets/_Project/Prefabs/Core/P_GameManager.prefab";
        public const string ReportPath = "Docs/WorldBuilder/MainPlayerValidation.txt";

        [MenuItem("Tools/Runway Panic/Integrate Gameplay Player in Main")]
        public static void Integrate()
        {
            RequireMain();
            // Preflight before changing anything. Never silently duplicate an existing player/manager.
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
            if (playerPrefab == null || managerPrefab == null)
                throw new InvalidOperationException("Pull S2's gameplay player and GameManager prefabs first.");
            var marker = GameObject.Find("GameplayMarkers/PlayerStart");
            var preview = SceneManager.GetActiveScene().GetRootGameObjects()
                .SingleOrDefault(root => root.name == "GreyboxPreview_TEMPORARY");
            if (marker == null || preview == null)
                throw new InvalidOperationException("Expected PlayerStart and the original preview root in Main.");
            var players = InScene<PlayerController>(true);
            var managers = InScene<GameManager>(true);
            if (players.Length > 1 || managers.Length > 1)
                throw new InvalidOperationException("Resolve duplicate gameplay players/managers before integration.");
            if (players.Length == 1 && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(players[0].gameObject) != PlayerPrefabPath)
                throw new InvalidOperationException("An unexpected player already exists. Review it; do not replace it automatically.");
            if (managers.Length == 1 && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(managers[0].gameObject) != ManagerPrefabPath)
                throw new InvalidOperationException("An unexpected GameManager already exists. Review it first.");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Integrate gameplay player and GameManager");
            try
            {
                if (players.Length == 0)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, SceneManager.GetActiveScene());
                    Undo.RegisterCreatedObjectUndo(instance, "Place gameplay player");
                    instance.transform.SetPositionAndRotation(marker.transform.position, marker.transform.rotation);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                    players = new[] { instance.GetComponent<PlayerController>() };
                }
                if (managers.Length == 0)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab, SceneManager.GetActiveScene());
                    Undo.RegisterCreatedObjectUndo(instance, "Place GameManager");
                    managers = new[] { instance.GetComponent<GameManager>() };
                }

                // Explicit scene binding; the shared prefab stays untouched.
                var managerData = new SerializedObject(managers[0]);
                managerData.FindProperty("playerLives").objectReferenceValue = players[0].GetComponent<PlayerLifeManager>();
                managerData.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(managers[0]);
                Undo.RecordObject(preview, "Disable temporary first-person preview");
                preview.SetActive(false);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Validate();
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                    throw new IOException("Main could not be saved.");
                Debug.Log("[MainPlayer] Saved connected S2 player/manager prefabs; preview retained but disabled.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
            finally { Undo.CollapseUndoOperations(undoGroup); }
        }

        [MenuItem("Tools/Runway Panic/Validate Main Player Integration")]
        public static void Validate()
        {
            RequireMain();
            var checks = new List<string> { "Main gameplay-player integration — saved-scene checks", "Unity: " + Application.unityVersion };
            int failed = 0;
            Action<bool, string> check = (ok, message) =>
            {
                checks.Add((ok ? "PASS: " : "FAIL: ") + message);
                if (!ok) failed++;
            };
            var scene = SceneManager.GetActiveScene();
            var players = InScene<PlayerController>(true);
            var managers = InScene<GameManager>(true);
            check(players.Length == 1, "Exactly one gameplay player, including inactive objects");
            check(managers.Length == 1, "Exactly one GameManager, including inactive objects");
            var preview = scene.GetRootGameObjects().SingleOrDefault(root => root.name == "GreyboxPreview_TEMPORARY");
            check(preview != null && !preview.activeSelf, "Temporary preview retained and disabled as a whole");
            check(InScene<Camera>().Count(camera => camera.enabled) == 1, "Exactly one enabled gameplay camera");
            check(InScene<AudioListener>().Count(listener => listener.enabled) == 1, "Exactly one enabled audio listener");
            var camera = InScene<Camera>().SingleOrDefault();
            check(camera != null && camera.CompareTag("MainCamera"), "Gameplay camera tagged MainCamera");

            if (players.Length == 1)
            {
                var player = players[0];
                check(player.gameObject.activeInHierarchy && player.enabled, "Gameplay controller active");
                check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player.gameObject) == PlayerPrefabPath,
                    "Player remains connected to S2's prefab");
                var marker = GameObject.Find("GameplayMarkers/PlayerStart");
                check(marker != null && Vector3.Distance(marker.transform.position, player.transform.position) < 0.001f,
                    "Saved player starts at PlayerStart");
                check(player.GetComponent<CharacterController>() != null && player.GetComponent<PlayerHealth>() != null &&
                    player.GetComponent<PlayerLifeManager>() != null && player.GetComponent<PlayerShooting>() != null,
                    "Original controller, health, lives and shooting components present");
                var input = player.GetComponent<PlayerInput>();
                check(input != null && input.actions != null && new[] { "Move", "Sprint", "Jump", "Attack" }
                    .All(action => input.actions.FindAction(action, false) != null), "Player Input actions assigned");
                check(camera != null && camera.transform.IsChildOf(player.transform), "Camera belongs to the gameplay prefab");
            }
            if (managers.Length == 1)
            {
                var manager = managers[0];
                check(manager.isActiveAndEnabled, "GameManager active");
                check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(manager.gameObject) == ManagerPrefabPath,
                    "GameManager remains connected to S2's prefab");
                var lives = new SerializedObject(manager).FindProperty("playerLives").objectReferenceValue;
                check(players.Length == 1 && lives == players[0].GetComponent<PlayerLifeManager>(),
                    "GameManager explicitly references this player's lives");
            }
            int missingScripts = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Sum(item => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject));
            check(missingScripts == 0, "No missing scripts in Main, including disabled preview");
            check(EditorBuildSettings.scenes.Any(item => item.enabled && item.path == ScenePath), "Main remains enabled in build settings");
            checks.Add("Failures: " + failed);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllLines(ReportPath, checks);
            if (failed != 0) throw new InvalidOperationException("Main player validation failed; see " + ReportPath);
        }

        public static T[] InScene<T>(bool includeInactive = false) where T : Component =>
            Object.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude)
                .Where(item => item.gameObject.scene == SceneManager.GetActiveScene()).ToArray();

        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open Main and stop Play mode first. No sandbox or prefab will be rewritten.");
        }
    }
}
