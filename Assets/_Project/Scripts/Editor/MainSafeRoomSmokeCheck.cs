using System;
using System.Linq;
using RunwayPanic.Agents.Crow;
using RunwayPanic.Core;
using RunwayPanic.Interaction;
using RunwayPanic.Player;
using RunwayPanic.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Short Play-mode lifecycle check. Runtime timer changes and movement are not saved.</summary>
    [InitializeOnLoad]
    public static class MainSafeRoomSmokeCheck
    {
        private const string Key = "RunwayPanic.TimedShelterSmoke";
        private static SafeRoomController room;
        private static ShelterZone zone;
        private static Door door;
        private static PlayerHealth player;
        private static CharacterController body;
        private static GameManager game;
        private static int phase, failures;
        private static double started;
        private static float beforePause;
        private static Vector3 closedPosition, closedForward;
        private static bool running;

        static MainSafeRoomSmokeCheck() => EditorApplication.playModeStateChanged += OnPlayMode;

        [MenuItem("Tools/Runway Panic/Run Timed Shelter Smoke Check")]
        private static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                phase = failures = 0;
                started = EditorApplication.timeSinceStartup;
                running = true;
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running) Finish("interrupted");
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            double elapsed = EditorApplication.timeSinceStartup - started;
            try
            {
                if (phase == 0 && elapsed >= 1)
                {
                    var rooms = UnityEngine.Object.FindObjectsByType<SafeRoomController>(FindObjectsInactive.Include);
                    Check(rooms.Length == 2 && rooms.All(r => r.enabled), "both room controllers enabled");
                    room = rooms.Single(r => r.name == "ShelterA_Controller");
                    zone = UnityEngine.Object.FindObjectsByType<ShelterZone>().Single(z => z.Group == "ShelterA");
                    door = GameObject.Find("Environment/Buildings/Shelter_A").GetComponentsInChildren<Door>().Single();
                    closedPosition = door.transform.position;
                    closedForward = door.transform.forward;
                    game = GameManager.Instance;
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                    body = player.GetComponent<CharacterController>();
                    var movement = player.GetComponent<PlayerController>();
                    if (movement != null) movement.enabled = false;
                    var waves = UnityEngine.Object.FindAnyObjectByType<WaveManager>();
                    if (waves != null) waves.enabled = false;
                    if (game.IsPaused) game.Resume();
                    if (game.CurrentState == GameState.MainMenu) game.ChangeState(GameState.Playing);
                    Check(game.CurrentState == GameState.Playing, "game is in Playing state");
                    var config = new SerializedObject(room);
                    config.FindProperty("useSeconds").floatValue = 3f;
                    config.FindProperty("cooldownSeconds").floatValue = 2f;
                    config.ApplyModifiedPropertiesWithoutUndo();
                    door.Open(zone.Centre);
                    MovePlayer(new Vector3(zone.Centre.x, .2f, zone.Centre.z));
                    Next();
                }
                else if (phase == 1 && elapsed >= 1.2)
                {
                    Check(room.Occupied, "player detected inside existing zone");
                    Check(ShelterZone.PlayerInside == zone, "Crow zone reports player inside");
                    Physics.SyncTransforms();
                    Vector3 outside = closedPosition + closedForward * 1.8f + Vector3.up * .4f;
                    Check(!Physics.CapsuleCast(outside, outside + Vector3.up * 1.1f, .35f, -closedForward, out _, 3.6f, ~0, QueryTriggerInteraction.Ignore), "open doorway has human-sized clearance");
                    door.Close();
                    Next();
                }
                else if (phase == 2 && elapsed >= .4)
                {
                    Check(room.State == SafeRoomController.ShelterState.Active, "closing while inside starts active use");
                    float before = room.RemainingSeconds;
                    door.Open(player.transform.position);
                    door.Close();
                    Check(room.State == SafeRoomController.ShelterState.Active && room.RemainingSeconds <= before + .01f, "brief reopen does not reset use");
                    beforePause = room.RemainingSeconds;
                    game.Pause();
                    Next();
                }
                else if (phase == 3 && elapsed >= .7)
                {
                    Check(game.IsPaused && Mathf.Abs(room.RemainingSeconds - beforePause) < .05f, "pause freezes timer");
                    game.Resume();
                    Next();
                }
                else if (phase == 4 && elapsed >= 3.2)
                {
                    Check(room.State == SafeRoomController.ShelterState.Cooldown && door.IsOpen && !door.IsLocked, "timeout opens unlocked door and starts cooldown");
                    door.Close();
                    Check(door.IsOpen && !door.IsLocked, "close attempt during cooldown immediately reopens");
                    Next();
                }
                else if (phase == 5 && elapsed >= 2.2)
                {
                    Check(room.State == SafeRoomController.ShelterState.Available, "cooldown expires");
                    door.Close();
                    Next();
                }
                else if (phase == 6 && elapsed >= .3)
                {
                    Check(room.State == SafeRoomController.ShelterState.Active, "room can be used again after cooldown");
                    MovePlayer(zone.Centre + Vector3.right * 12f);
                    Next();
                }
                else if (phase == 7 && elapsed >= .6)
                {
                    Check(!room.Occupied && room.State == SafeRoomController.ShelterState.Cooldown && door.IsOpen, "leaving early starts cooldown and opens door");
                    Check(ShelterZone.PlayerInside != zone, "Crow zone clears when player leaves");
                    Finish("completed");
                }
            }
            catch (Exception error) { Check(false, error.ToString()); Finish("exception"); }
        }

        private static void MovePlayer(Vector3 position)
        {
            body.enabled = false;
            player.transform.position = position;
            body.enabled = true;
            Physics.SyncTransforms();
        }

        private static void Next() { phase++; started = EditorApplication.timeSinceStartup; }
        private static void Check(bool passed, string description)
        {
            if (!passed) failures++;
            Debug.Log("[TimedShelterSmoke] " + (passed ? "PASS: " : "FAIL: ") + description);
        }
        private static void Finish(string reason)
        {
            running = false;
            EditorApplication.update -= Tick;
            Debug.Log("[TimedShelterSmoke] " + reason + "; failures=" + failures + ". Runtime test only; E input and falling FOD not tested.");
            SessionState.SetBool(Key, false);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
