using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>Six autonomous-bird attempts: three stationary, three scripted straight-walking. No balance edits.</summary>
    [InitializeOnLoad]
    public static class GullAccuracyCheck
    {
        private const string Key = "RunwayPanic.GullAccuracy";
        private const string ReportPath = "Docs/IS/Gull/AccuracySamples.md";
        private static readonly List<string> Rows = new List<string>();
        private static readonly int[] Attempts = new int[2], Resolved = new int[2], Hits = new int[2];
        private static readonly float[] MissTotals = new float[2];
        private static bool running, dropped, impacted, settled, hit, controllerWasEnabled;
        private static int trial, lastFrame;
        private static double began;
        private static float trialStart, warningTime, warningSpeed, releaseDelay, miss, hpLoss;
        private static GullBrain bird;
        private static PlayerHealth player;
        private static PlayerController controller;
        private static CharacterController capsule;
        private static GameObject prefab;

        static GullAccuracyCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }
        [MenuItem("Tools/IS/Gull/Measure Six Accuracy Attempts")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != GullG1Setup.SandboxPath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save/open the Gull sandbox outside Play first.");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                running = true; trial = lastFrame = -1; began = EditorApplication.timeSinceStartup;
                bird = null; controller = null; player = null; Rows.Clear();
                Array.Clear(Attempts, 0, 2); Array.Clear(Resolved, 0, 2); Array.Clear(Hits, 0, 2); Array.Clear(MissTotals, 0, 2);
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running) Finish("Interrupted", false);
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
        }
        private static void StartTrial()
        {
            if (bird != null) { bird.gameObject.SetActive(false); UnityEngine.Object.Destroy(bird.gameObject); }
            FodManager.Instance.ClearAll(); GullKettle.Instance.Clear();
            foreach (var supply in UnityEngine.Object.FindObjectsByType<FodSpawnPoint>(FindObjectsSortMode.None))
            { supply.enabled = true; supply.ResetForWave(); }
            capsule.enabled = false; player.transform.position = new Vector3(0, .12f, 0); capsule.enabled = true;
            player.ResetHealth();
            GullDebugStats.EnsureExists(); GullDebugStats.Instance.ResetStatistics(); GullDebugStats.Instance.Visible = false;
            dropped = impacted = settled = hit = false; warningTime = -1f; warningSpeed = releaseDelay = miss = hpLoss = 0f;
            trial++; Attempts[trial / 3]++; trialStart = Time.time;
            Vector3 start = trial % 3 == 1 ? new Vector3(8.5f, 3, 10) : new Vector3(-8.5f, 3, trial % 3 == 2 ? 10 : -10);
            bird = UnityEngine.Object.Instantiate(prefab, start, Quaternion.identity).GetComponent<GullBrain>();
            bird.TelegraphStarted += source =>
            {
                warningTime = Time.time;
                Vector3 velocity = PlayerEventHub.Instance.PlayerVelocity;
                warningSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            };
            bird.FodDropped += (item, target) =>
            {
                dropped = true; releaseDelay = Time.time - warningTime;
                item.Impacted += (piece, wasHit) =>
                {
                    impacted = true; hit = wasHit;
                    Vector3 delta = piece.transform.position - player.transform.position;
                    miss = new Vector2(delta.x, delta.z).magnitude;
                    hpLoss = player.MaxHP - player.CurrentHP;
                };
                item.Settled += piece => settled = true;
            };
            WriteReport("Running attempt " + (trial + 1) + "/6");
        }
        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 220) { Finish("Wall-clock limit reached", true); return; }
                if (trial < 0)
                {
                    if (EditorApplication.timeSinceStartup - began < 1) return;
                    if (UnityEngine.Object.FindObjectsByType<GullBrain>(FindObjectsSortMode.None).Length != 0)
                        throw new InvalidOperationException("Use a fresh Play session without pre-spawned Gulls.");
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                    controller = player.GetComponent<PlayerController>(); capsule = player.GetComponent<CharacterController>();
                    controllerWasEnabled = controller.enabled; controller.enabled = false;
                    BirdStateLabel.Visible = false;
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullG1Setup.PrefabPath);
                    StartTrial(); return;
                }
                bool attacking = bird.State == GullState.DescendToDrop || bird.State == GullState.Telegraph || bird.State == GullState.Drop || dropped;
                // Same movement fixture as G4; only during approach/warning/fall, never teleport to chase a drop.
                if (trial >= 3 && attacking) capsule.Move(Vector3.forward * (2f * Time.deltaTime));
                if (impacted && settled)
                {
                    var stats = GullDebugStats.Instance;
                    if (stats.Drops != 1 || stats.ResolvedDrops != 1 || stats.PlayerHits != (hit ? 1 : 0) ||
                        stats.MissDistanceSamples != 1 || Mathf.Abs(stats.AverageMissDistance - miss) > .01f)
                        throw new InvalidOperationException("Overlay and observed first-impact measurements disagree.");
                    int group = trial / 3; Resolved[group]++; if (hit) Hits[group]++; MissTotals[group] += miss;
                    Rows.Add("| " + (trial + 1) + " | " + (group == 0 ? "Standing" : "Straight walk") + " | Resolved | " +
                        (hit ? "Yes" : "No") + " | " + F(hpLoss) + " | " + F(miss) + " | " + F(warningSpeed) + " | " + F(releaseDelay) + " |");
                    if (trial == 5) Finish("Completed all six attempts; overlay counters matched every resolved impact", true);
                    else StartTrial();
                }
                else if (Time.time - trialStart > 32f)
                {
                    Rows.Add("| " + (trial + 1) + " | " + (trial < 3 ? "Standing" : "Straight walk") + " | Timed out: " + bird.State + " | -- | -- | -- | -- | -- |");
                    if (trial == 5) Finish("Completed attempts, including reported timeouts", true); else StartTrial();
                }
            }
            catch (Exception error) { Finish("ERROR: " + error.Message, true); }
        }
        private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
        private static void WriteReport(string status)
        {
            var lines = new List<string>
            {
                "# Gull — small autonomous accuracy sample", "", "Status: " + status, "",
                "Report updated (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ".",
                "Unity " + Application.unityVersion + "; " + SystemInfo.operatingSystem + "; " + SystemInfo.processorType + ".",
                "Editor sandbox, one actual Gull per attempt, natural FOD collection/kettle/telegraph/drop. No forced attack state or fabricated drops.",
                "Three starts per condition: (-8.5,3,-10), (8.5,3,10), (-8.5,3,10). Player resets to (0,0.12,0) and full HP between attempts.",
                "Standing versus scripted +Z walking commanded at 2 m/s from attack approach until impact. Movement is driven once per observed Play frame from the Editor; measured hub speed at the warning is recorded below. Not a human-input test or guaranteed frame-perfect locomotion.",
                "No deliberate shooting, shield activation or shelter use. One piece per attempt; debris/supply/kettle state and debug counters reset between attempts. Other bird prefabs/species/balance and saved scenes unchanged.", "",
                "| Attempt | Condition | Outcome | HP hit | HP loss | First-impact-to-player (m) | Hub speed at warning (m/s) | Warning to release (s) |",
                "|---|---|---|---|---:|---:|---:|---:|"
            };
            lines.AddRange(Rows); lines.Add("");
            for (int i = 0; i < 2; i++)
                lines.Add((i == 0 ? "Standing" : "Straight walking") + ": " + Attempts[i] + " attempts; " + Resolved[i] + " resolved impacts; " + Hits[i] + " HP hits; resolved-impact hit rate " +
                    (Resolved[i] > 0 ? F(Hits[i] * 100f / Resolved[i]) + "%" : "n/a") + "; average impact-to-player distance " +
                    (Resolved[i] > 0 ? F(MissTotals[i] / Resolved[i]) + " m" : "n/a") + ". Timeouts are not silently counted as resolved misses.");
            lines.Add(""); lines.Add("Only three attempts per condition: descriptive functional evidence, not a statistically reliable hit rate or final wave balance. No eight-Gull Profiler measurement is made here.");
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines(ReportPath, lines);
        }
        private static void Finish(string status, bool stopPlay)
        {
            running = false; EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            if (bird != null) bird.gameObject.SetActive(false);
            if (controller != null) controller.enabled = controllerWasEnabled;
            WriteReport(status); Debug.Log("[GullAccuracy] " + status + "; see " + ReportPath);
            if (stopPlay) EditorApplication.isPlaying = false;
        }
    }
}
