using System;
using System.Collections.Generic;
using System.IO;
using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>One diagnostic fixture run, not AI accuracy/performance evidence.</summary>
    [InitializeOnLoad]
    public static class GullG6SmokeCheck
    {
        private const string Key = "RunwayPanic.GullG6Smoke";
        private const string PreviewKey = "RunwayPanic.GullG6Preview";
        private static readonly List<string> Report = new List<string>();
        private static bool running;
        private static int stage, failures;
        private static double began;
        private static GullDebugStats stats;
        private static GameObject fodPrefab;

        static GullG6SmokeCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }
        [MenuItem("Tools/IS/Gull/Preview or Toggle Stats Overlay")]
        public static void ToggleOverlay()
        {
            if (!EditorApplication.isPlaying)
            {
                SessionState.SetBool(PreviewKey, true);
                EditorApplication.isPlaying = true;
                return;
            }
            GullDebugStats.EnsureExists();
            if (GullDebugStats.Instance != null) GullDebugStats.Instance.Visible = !GullDebugStats.Instance.Visible;
        }
        [MenuItem("Tools/IS/Gull/Stop Stats Preview")]
        public static void StopPreview() { EditorApplication.isPlaying = false; }
        [MenuItem("Tools/IS/Gull/Run Focused G6 Stats Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != GullG1Setup.SandboxPath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Open/save the Gull sandbox outside Play first.");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PreviewKey, false))
            {
                SessionState.SetBool(PreviewKey, false);
                GullDebugStats.EnsureExists();
                if (GullDebugStats.Instance != null) GullDebugStats.Instance.Visible = true;
            }
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                stage = failures = 0; running = true; began = EditorApplication.timeSinceStartup;
                Report.Clear(); Report.Add("Gull G6 diagnostic fixture check — Unity " + Application.unityVersion);
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running) Finish("Interrupted");
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
        }
        private static FodItem DropAt(Vector3 position)
        {
            var item = UnityEngine.Object.Instantiate(fodPrefab, position, Quaternion.identity).GetComponent<FodItem>();
            item.Release(); GullDebugStats.RecordDrop(item); return item;
        }
        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 15) throw new TimeoutException("Stats fixture did not finish within 15 seconds.");
                if (EditorApplication.timeSinceStartup - began < 1) return;
                if (stage == 0)
                {
                    var player = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                    player.GetComponent<PlayerController>().enabled = false;
                    var capsule = player.GetComponent<CharacterController>();
                    capsule.enabled = false; player.transform.position = new Vector3(0, .12f, 0); capsule.enabled = true;
                    player.ResetHealth();
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullG1Setup.PrefabPath);
                    var a = UnityEngine.Object.Instantiate(prefab, new Vector3(-8, 12, 0), Quaternion.identity).GetComponent<GullBrain>();
                    var b = UnityEngine.Object.Instantiate(prefab, new Vector3(8, 12, 0), Quaternion.identity).GetComponent<GullBrain>();
                    a.enabled = b.enabled = false; // Only counter fixtures, not a timed AI run.
                    GullDebugStats.EnsureExists(); stats = GullDebugStats.Instance;
                    var original = stats; GullDebugStats.EnsureExists();
                    Check(original != null && stats == GullDebugStats.Instance && UnityEngine.Object.FindObjectsByType<GullDebugStats>(FindObjectsSortMode.None).Length == 1, "Exactly one scene stats component");
                    stats.ResetStatistics(); stats.Visible = true;
                    Check(stats.ResolvedDrops == 0 && stats.HitRate == 0f && stats.AverageMissDistance == 0f, "Empty session metrics are finite zero values");
                    GullDebugStats.RecordTokenGrant(a); GullDebugStats.RecordTokenGrant(a);
                    Check(stats.TokenHandOffs == 0, "Initial/repeated same-owner grants are not hand-offs");
                    GullDebugStats.RecordTokenGrant(b);
                    Check(stats.TokenHandOffs == 1, "Different-owner grant records one hand-off");
                    fodPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullFodSetup.PrefabPath);
                    var first = DropAt(player.transform.position + Vector3.up * 4f);
                    GullDebugStats.RecordDrop(first); // Duplicate notifications must not double-count.
                    DropAt(player.transform.position + new Vector3(8, 4, 0));
                    Check(stats.Drops == 2 && stats.PendingDrops == 2, "Two physical drops tracked once each");
                    stage = 1;
                }
                else if (stats.ResolvedDrops == 2 && stats.GroundFodCount == 2)
                {
                    Check(stats.PlayerHits == 1 && Mathf.Approximately(stats.HitRate, .5f), "One real HP hit plus one miss gives 1/2 resolved impacts");
                    Check(stats.MissDistanceSamples == 2 && stats.AverageMissDistance > 3.5f && stats.AverageMissDistance < 4.5f, "Impact-to-player distance samples both real physics contacts");
                    Report.Add("FIXTURE ONLY: mean impact-to-player=" + stats.AverageMissDistance.ToString("F2") + " m; hits=" + stats.PlayerHits + "/" + stats.ResolvedDrops);
                    Check(stats.PendingDrops == 0 && stats.HandOffsPerMinute > 0f, "Resolved drops unsubscribe and active-time hand-off rate is finite");
                    var cancelled = DropAt(new Vector3(0, 10, 8)); cancelled.Despawn();
                    Check(stats.PendingDrops == 0 && stats.ResolvedDrops == 2, "Despawning a pending drop removes tracking without fabricating an impact");
                    stats.ResetStatistics();
                    Check(stats.Drops == 0 && stats.TokenHandOffs == 0 && stats.ResolvedDrops == 0 && stats.GroundFodCount == 2, "Reset clears evidence counters without deleting actual ground debris");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(string error)
        {
            running = false; EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            if (error != null) Check(false, error);
            Report.Add("Controlled physical drops and diagnostic token-grant calls only. Not autonomous-Gull hit-rate or eight-Gull Profiler evidence. Keyboard/visual behaviour is separate from this counter fixture.");
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines("Docs/IS/Gull/G6Validation.txt", Report);
            Debug.Log("[GullG6Check] Finished with " + failures + " failures; see G6Validation.txt.");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
