using System;
using System.Collections.Generic;
using System.IO;
using RunwayPanic.Core;
using RunwayPanic.Player;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RunwayPanic.EditorTools
{
    /// <summary>Opt-in end-to-end checks using real trigger callbacks, shared health/lives and the real respawn delay.</summary>
    [InitializeOnLoad]
    public static class MainCheckpointPlaytest
    {
        private const string SessionKey = "RunwayPanic.MainCheckpointPlaytest";
        private const string ReportPath = "Docs/WorldBuilder/CheckpointPlaytest.txt";
        private static readonly List<string> Report = new List<string>();
        private static PlayerController player;
        private static CharacterController controller;
        private static PlayerRespawn respawn;
        private static PlayerHealth health;
        private static PlayerLifeManager lives;
        private static Transform checkpoint;
        private static int phase, index, failed;
        private static double began, phaseStarted;
        private static bool running;

        static MainCheckpointPlaytest() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Tools/Runway Panic/Run Main Checkpoint Playtest")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save Main before running the checkpoint test.");
            MainCheckpointIntegration.Validate();
            SessionState.SetBool(SessionKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                phase = index = failed = 0; running = true;
                began = phaseStarted = EditorApplication.timeSinceStartup;
                Report.Clear();
                Report.Add("Main checkpoint integration — real trigger / death / delayed respawn checks");
                Report.Add("Unity: " + Application.unityVersion);
                Report.Add("CharacterController walks into each trigger; no direct SetCheckpoint calls or private-field changes.");
                Report.Add("Lethal damage is supplied by the test, not a bird. Teleports/test deaths remain Play-mode-only.");
                EditorApplication.update += Tick;
                Application.logMessageReceived += OnLog;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && running)
            {
                Check(false, "Test interrupted before completion");
                Finish(false);
            }
            else if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(SessionKey, false);
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - began > 45) { Check(false, "Completed all checkpoint checks within 45 seconds"); Finish(); return; }
            try
            {
                double elapsed = now - phaseStarted;
                switch (phase)
                {
                    case 0:
                        if (elapsed < 2) return;
                        player = Object.FindAnyObjectByType<PlayerController>();
                        if (player == null || GameManager.Instance == null) throw new InvalidOperationException("Gameplay player/manager missing.");
                        controller = player.GetComponent<CharacterController>();
                        respawn = player.GetComponent<PlayerRespawn>();
                        health = player.GetComponent<PlayerHealth>();
                        lives = player.GetComponent<PlayerLifeManager>();
                        Check(respawn.CurrentCheckpoint == null, "New session starts without an activated checkpoint");
                        Check(lives.CurrentLives == 3 && health.IsAlive, "Fresh player starts alive with three lives");
                        Check(player.IsGrounded, "Player starts grounded at the original PlayerStart");
                        BeginApproach();
                        Next(); break;
                    case 1:
                        // Physically cross the trigger edge; do not call its handler or SetCheckpoint.
                        Vector3 delta = checkpoint.position - player.transform.position;
                        delta.y = 0;
                        controller.Move(Vector3.ClampMagnitude(delta, 0.15f) + Vector3.down * 0.02f);
                        if (HorizontalDistance(player.transform.position, checkpoint.position) > 0.2f || elapsed < 0.5) return;
                        Next(); break;
                    case 2:
                        if (elapsed < 0.25) return;
                        Check(respawn.CurrentCheckpoint == checkpoint, checkpoint.name + " activates through an actual trigger entry");
                        Check(health.IsAlive && lives.CurrentLives == 3 - index, "Entering checkpoint does not consume a life or damage the player");
                        Teleport(new Vector3(0, 0.15f, -24), Quaternion.identity);
                        Check(HorizontalDistance(player.transform.position, checkpoint.position) > 5, "Test death occurs away from " + checkpoint.name);
                        player.enabled = true;
                        health.TakeDamage(health.MaxHP, WeaponRarity.Common);
                        Check(!health.IsAlive && lives.CurrentLives == 2 - index, "Death consumes exactly one life");
                        Check(!player.enabled, "Movement is disabled while waiting to respawn");
                        Next(); break;
                    case 3:
                        if (!health.IsAlive)
                        {
                            if (elapsed > 5) throw new InvalidOperationException("Player did not respawn within five seconds.");
                            return;
                        }
                        Check(elapsed >= 1.5, "Respawn respects the supplied delay instead of teleporting immediately");
                        Check(HorizontalDistance(player.transform.position, checkpoint.position) < 0.25f,
                            "Player returns to " + checkpoint.name + " after death");
                        Check(Mathf.Abs(player.transform.position.y - checkpoint.position.y) < 0.25f,
                            checkpoint.name + " respawn is at floor level");
                        Check(Quaternion.Angle(player.transform.rotation, checkpoint.rotation) < 1,
                            checkpoint.name + " respawn faces the runway");
                        Check(health.CurrentHP == health.MaxHP && player.enabled, "Respawn restores full HP and movement");
                        Check(lives.CurrentLives == 2 - index && respawn.CurrentCheckpoint == checkpoint,
                            "Respawn preserves remaining lives and the active checkpoint");
                        Next(); break;
                    case 4:
                        if (elapsed < 0.35) return;
                        Check(player.IsGrounded && player.transform.position.y > -0.1f, checkpoint.name + " respawn settles on solid ground");
                        if (index == 0)
                        {
                            index = 1;
                            BeginApproach();
                            phase = 1; phaseStarted = now;
                        }
                        else
                        {
                            health.TakeDamage(health.MaxHP, WeaponRarity.Common);
                            Next();
                        }
                        break;
                    case 5:
                        if (elapsed < 2.5) return;
                        Check(lives.CurrentLives == 0 && !health.IsAlive, "Last death exhausts lives; checkpoint does not grant free respawns");
                        Check(GameManager.Instance.CurrentState == GameState.GameOver && !player.enabled,
                            "Last death enters GameOver and keeps player movement disabled");
                        Finish(); break;
                }
            }
            catch (Exception exception) { Check(false, exception.ToString()); Finish(); }
        }

        private static void BeginApproach()
        {
            checkpoint = MainCheckpointIntegration.FindMarker(MainCheckpointIntegration.MarkerNames[index]).transform;
            player.enabled = false; // Drive the actual controller reproducibly; restore through S2's respawn.
            Teleport(checkpoint.position - checkpoint.forward * 3.5f, checkpoint.rotation);
            Check(respawn.CurrentCheckpoint != checkpoint, checkpoint.name + " approach starts outside its trigger");
        }

        private static void Teleport(Vector3 position, Quaternion rotation)
        {
            Vector3 delta = position - player.transform.position;
            controller.enabled = false;
            player.transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            Physics.SyncTransforms();
            CinemachineCore.OnTargetObjectWarped(player.transform, delta);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private static void Next() { phase++; phaseStarted = EditorApplication.timeSinceStartup; }
        private static void Check(bool ok, string text) { Report.Add((ok ? "PASS: " : "FAIL: ") + text); if (!ok) failed++; }
        private static void OnLog(string text, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Check(false, "Runtime " + type + ": " + text + "\n" + trace);
        }
        private static void Finish(bool stopPlay = true)
        {
            running = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Report.Add("No checkpoint HUD, save-to-disk persistence or bird damage source is tested here.");
            Report.Add("Failures: " + failed);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllLines(ReportPath, Report);
            Debug.Log("[MainCheckpointPlaytest] " + ReportPath + "; failures=" + failed);
            if (stopPlay) EditorApplication.isPlaying = false;
        }
    }
}
