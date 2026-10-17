using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Core;
using RunwayPanic.Player;
using RunwayPanic.UI;
using RunwayPanic.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RunwayPanic.EditorTools
{
    /// <summary>Opt-in editor Play-mode smoke checks. Test input/targets never enter the saved scene or a build.</summary>
    [InitializeOnLoad]
    public static class MainPlayerSmokeTest
    {
        private const string SessionKey = "RunwayPanic.MainPlayerSmokeTest";
        private const string BatchKey = SessionKey + ".Batch";
        private const string ReportPath = "Docs/WorldBuilder/MainPlayerPlaytest.txt";
        private static readonly List<string> Report = new List<string>();
        private static int failures, phase;
        private static double began, phaseStarted;
        private static bool running;
        private static PlayerController player;
        private static GameManager manager;
        private static Keyboard keyboard;
        private static Mouse mouse;
        private static Vector3 referencePosition;
        private static float walkDistance;

        static MainPlayerSmokeTest() { EditorApplication.playModeStateChanged += OnPlayMode; }

        // Command line entry: Unity -batchmode -projectPath ... -executeMethod ...RunBatch (NO -quit).
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(MainPlayerIntegration.ScenePath);
            MainPlayerIntegration.Integrate();
            SessionState.SetBool(BatchKey, true);
            Start();
        }

        [MenuItem("Tools/Runway Panic/Run Main Player Smoke Test")]
        public static void RunInteractive()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main and stop Play first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save Main before testing. This test never saves Play-mode changes.");
            MainPlayerIntegration.Validate();
            SessionState.SetBool(BatchKey, false);
            Start();
        }

        private static void Start()
        {
            SessionState.SetBool(SessionKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Report.Clear(); failures = 0; phase = 0; running = true;
                Report.Add("Main player integration — automated Editor Play-mode smoke checks");
                Report.Add("Unity: " + Application.unityVersion);
                Report.Add("Movement uses a temporary paired Input System keyboard; shooting uses TryShoot, pause uses GameManager/UI APIs.");
                Report.Add("No test devices, targets, teleports or runtime state are saved into Main.");
                began = phaseStarted = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && running)
            {
                Check(false, "Smoke test interrupted before completing all checks");
                Finish(false);
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(SessionKey, false);
                if (SessionState.GetBool(BatchKey, false))
                    EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetInt(SessionKey + ".Failures", 1) == 0 ? 0 : 1);
            }
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - began > 40) { Check(false, "Test completed within 40 seconds"); Finish(); return; }
            try
            {
                double elapsed = now - phaseStarted;
                switch (phase)
                {
                    case 0:
                        if (elapsed < 2) return;
                        player = Object.FindAnyObjectByType<PlayerController>();
                        manager = GameManager.Instance;
                        if (player == null || manager == null) throw new InvalidOperationException("Player or manager failed to start.");
                        Check(MainPlayerIntegration.InScene<PlayerController>().Length == 1, "One active gameplay player after startup");
                        Check(MainPlayerIntegration.InScene<GameManager>().Length == 1, "One active GameManager after startup");
                        Check(Camera.allCamerasCount == 1 && Camera.main != null, "One active camera; Camera.main resolves");
                        Check(Object.FindObjectsByType<AudioListener>().Count(item => item.isActiveAndEnabled) == 1,
                            "One active audio listener after startup");
                        Check(manager.CurrentState == GameState.Playing && Time.timeScale == 1, "Game begins in Playing, unpaused");
                        Check(player.IsGrounded && player.transform.position.y > -0.1f, "Player settles on airport ground");
                        Check(player.GetComponent<PlayerHealth>().CurrentHP == 100 && player.GetComponent<PlayerLifeManager>().CurrentLives == 3,
                            "Player starts with 100 HP and 3 lives");
                        Check(player.GetComponent<PlayerWeaponHolder>().CurrentWeapon != null, "Starting weapon equipped through the supplied prefab");
                        Check(Object.FindAnyObjectByType<EventSystem>() != null, "UI EventSystem created by supplied UI");
                        keyboard = InputSystem.AddDevice<Keyboard>("MainIntegrationTestKeyboard");
                        mouse = InputSystem.AddDevice<Mouse>("MainIntegrationTestMouse");
                        player.GetComponent<PlayerInput>().SwitchCurrentControlScheme(keyboard, mouse);
                        referencePosition = player.transform.position;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                        Next(); break;
                    case 1:
                        if (elapsed < 1) return;
                        walkDistance = HorizontalDistance(referencePosition, player.transform.position);
                        Check(walkDistance > 2 && walkDistance < 7, "W input moves the actual player on the runway (" + walkDistance.ToString("F2") + " m)");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        Next(); break;
                    case 2:
                        if (elapsed < 0.25) return;
                        referencePosition = player.transform.position;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                        Next(); break;
                    case 3:
                        if (elapsed < 0.2) return;
                        Check(player.transform.position.y > referencePosition.y + 0.25f, "Space input makes the grounded player jump");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        Next(); break;
                    case 4:
                        if (elapsed < 1.1) return;
                        Check(player.IsGrounded, "Player lands again after jumping");
                        referencePosition = player.transform.position;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                        Next(); break;
                    case 5:
                        if (elapsed < 1) return;
                        float sprintDistance = HorizontalDistance(referencePosition, player.transform.position);
                        Check(sprintDistance > walkDistance * 1.2f, "Shift+W runs faster than walking (" + sprintDistance.ToString("F2") + " m)");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        Next(); break;
                    case 6:
                        if (elapsed < 0.35) return;
                        TestShooting();
                        manager.Pause();
                        Next(); break;
                    case 7:
                        if (elapsed < 0.25) return;
                        Check(manager.IsPaused && Time.timeScale == 0, "Pause freezes gameplay time");
                        var menu = Object.FindAnyObjectByType<MenuScreensUI>();
                        if (menu == null) throw new InvalidOperationException("GameManager's menu component is missing.");
                        var menuData = new SerializedObject(menu);
                        var pauseScreen = (GameObject)menuData.FindProperty("pauseScreen").objectReferenceValue;
                        Check(pauseScreen.activeInHierarchy, "Supplied pause screen becomes visible");
                        var resume = (Button)menuData.FindProperty("resumeButton").objectReferenceValue;
                        resume.onClick.Invoke();
                        Next(); break;
                    case 8:
                        if (elapsed < 0.25) return;
                        Check(!manager.IsPaused && Time.timeScale == 1, "Supplied Resume button returns to Playing");
                        TestPassages();
                        Report.Add("Not tested automatically: mouse-look feel/camera occlusion at every angle, manual mouse-click aim, bird combat, waves, shop, pickups and checkpoint activation.");
                        Finish(); break;
                }
            }
            catch (Exception exception)
            {
                Check(false, exception.ToString());
                Finish();
            }
        }

        private static void TestShooting()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "MainIntegrationTarget_RUNTIME_ONLY";
            try
            {
                target.transform.position = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f)).GetPoint(8);
                target.transform.localScale = Vector3.one * 1.5f;
                var dummy = target.AddComponent<DamageDummy>();
                Physics.SyncTransforms();
                var weapon = player.GetComponent<PlayerWeaponHolder>().CurrentWeapon;
                int ammo = weapon.AmmoInMagazine;
                float hp = dummy.CurrentHP;
                bool fired = player.GetComponent<PlayerShooting>().TryShoot();
                Check(fired && weapon.AmmoInMagazine == ammo - 1, "TryShoot fires equipped weapon and consumes one round");
                Check(dummy.CurrentHP < hp, "Integrated camera/muzzle raycasts damage a temporary target");
            }
            finally { Object.DestroyImmediate(target); }
        }

        private static void TestPassages()
        {
            player.enabled = false;
            var controller = player.GetComponent<CharacterController>();
            Vector3 original = player.transform.position;
            try
            {
                foreach (string name in new[] { "Shelter_A", "Shelter_B" })
                {
                    var root = GameObject.Find(name).transform;
                    Traverse(controller, root.TransformPoint(new Vector3(0, 0.15f, -6)),
                        root.TransformPoint(new Vector3(0, 0.15f, 0)), name + " open entrance");
                }
                var hangar = GameObject.Find("Hangar_Greybox").transform;
                Traverse(controller, hangar.TransformPoint(new Vector3(10, 0.15f, 0)),
                    hangar.TransformPoint(new Vector3(0, 0.15f, 0)), "Hangar main opening");
                Traverse(controller, hangar.TransformPoint(new Vector3(0, 0.15f, 0)),
                    hangar.TransformPoint(new Vector3(0, 0.15f, 15)), "Hangar side exit");
            }
            finally
            {
                controller.enabled = false;
                player.transform.position = original;
                controller.enabled = true;
                player.enabled = true;
            }
        }

        private static void Traverse(CharacterController controller, Vector3 start, Vector3 end, string label)
        {
            controller.enabled = false;
            controller.transform.position = start;
            controller.enabled = true;
            Physics.SyncTransforms();
            Vector3 step = (end - start) / 100;
            for (int i = 0; i < 100; i++) controller.Move(step);
            float error = HorizontalDistance(controller.transform.position, end);
            Check(error < 0.3f, "Actual gameplay CharacterController traverses " + label + " (error " + error.ToString("F2") + " m)");
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private static void Next() { phase++; phaseStarted = EditorApplication.timeSinceStartup; }
        private static void Check(bool ok, string message)
        {
            Report.Add((ok ? "PASS: " : "FAIL: ") + message);
            if (!ok) failures++;
        }
        private static void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Check(false, "Runtime " + type + ": " + message + "\n" + trace);
        }
        private static void Finish(bool stopPlay = true)
        {
            running = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllLines(ReportPath, Report);
            SessionState.SetInt(SessionKey + ".Failures", failures);
            Debug.Log("[MainPlayerSmoke] " + ReportPath + "; failures=" + failures);
            if (stopPlay) EditorApplication.isPlaying = false;
        }
    }
}
