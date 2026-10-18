using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>One Main-scene runtime check using temporary birds and explicit game-state hooks, not a production wave loop.</summary>
    [InitializeOnLoad]
    public static class MainGullSmokeCheck
    {
        private const string Key = "RunwayPanic.MainGullSmoke";
        private static readonly List<string> Report = new List<string>();
        private static bool running, dropped, settled, controllerWasEnabled;
        private static int stage, failures;
        private static double began;
        private static float distressTime;
        private static GullBrain[] birds, responders;
        private static GullKettle kettle;
        private static PlayerController controller;
        private static PlayerHealth player;

        static MainGullSmokeCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }
        [MenuItem("Tools/IS/Gull/Run Focused Main Gull Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainGullIntegration.MainPath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Open/save connected Main outside Play first.");
            MainGullIntegration.Validate(); SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                running = true; stage = failures = 0; dropped = settled = false;
                began = EditorApplication.timeSinceStartup; controller = null; birds = responders = null; Report.Clear();
                Report.Add("Main Gull — focused real-scene check, Unity " + Application.unityVersion);
                Report.Add("Temporary birds spawned from the shared species asset at real Spawn markers. Fixture calls BeginWave/CompleteWave explicitly; no automatic wave controller exists yet.");
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running) { Check(false, "Interrupted"); Finish(false); }
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
        }
        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 70) throw new TimeoutException("Main check timed out at stage " + stage);
                if (EditorApplication.timeSinceStartup - began < 1) return;
                var game = GameManager.Instance;
                if (player != null && player.CurrentHP < player.MaxHP * .25f) player.ResetHealth();
                if (stage == 0)
                {
                    if (MainGullIntegration.InMain<BirdHealth>().Length != 0) throw new InvalidOperationException("Unexpected pre-spawned birds; refusing to interfere.");
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                    controller = player.GetComponent<PlayerController>(); controllerWasEnabled = controller.enabled; controller.enabled = false;
                    var capsule = player.GetComponent<CharacterController>();
                    capsule.enabled = false; player.transform.position = new Vector3(0, .12f, 0); capsule.enabled = true; player.ResetHealth();
                    var points = new List<BirdPoint>(); BirdPoint.GetAll(BirdPointType.Spawn, points);
                    points = points.OrderBy(p => p.name).ToList();
                    Check(points.Count == 8 && MainGullIntegration.InMain<FodSpawnPoint>().Count(p => p.IsReady) == 8, "Main exposes eight real spawn markers and eight ready FOD supplies");
                    if (!game.BeginWave(1)) throw new InvalidOperationException("Could not begin fixture wave through GameManager.");
                    kettle = GullKettle.Instance; kettle.DispatchEnabled = false;
                    var species = AssetDatabase.LoadAssetAtPath<BirdSpeciesData>(MainGullIntegration.SpeciesPath);
                    birds = new GullBrain[3];
                    for (int i = 0; i < birds.Length; i++)
                    {
                        birds[i] = UnityEngine.Object.Instantiate(species.Prefab, points[i * 2].Position, Quaternion.identity).GetComponent<GullBrain>();
                        birds[i].FodDropped += (item, aim) => { dropped = true; item.Settled += piece => settled = true; };
                    }
                    Check(birds.All(b => b != null && b.IsAlive && b.Config != null) && BirdRegistry.CountAlive(BirdSpecies.Gull) == 3,
                        "Three real Gull prefab instances resolve through the shared species asset and registry");
                    stage = 1;
                }
                else if (stage == 1 && birds.All(b => b.State == GullState.InKettle && b.CarriedFod != null))
                {
                    Check(kettle.Members.Count == 3 && birds.Select(b => b.CarriedFod).Distinct().Count() == 3,
                        "All three collect distinct actual FOD and form Main's kettle");
                    kettle.DispatchEnabled = true; stage = 2;
                }
                else if (stage == 2 && dropped && settled)
                {
                    Check(FodManager.Instance.LandedCount > 0, "A natural token-controlled drop reaches Main scenery and becomes managed debris");
                    kettle.DispatchEnabled = false;
                    var source = kettle.TokenHolder != null && kettle.TokenHolder.CarriedFod != null ? kettle.TokenHolder : birds.First(b => b.CarriedFod != null);
                    responders = birds.Where(b => Vector3.Distance(b.transform.position, source.transform.position) <= source.Config.distressRadius).ToArray();
                    var carried = responders.Select(b => b.CarriedFod).ToArray(); var oldHolder = kettle.TokenHolder;
                    var health = source.GetComponent<BirdHealth>(); var hub = PlayerEventHub.Instance;
                    typeof(PlayerEventHub).GetMethod("HandleShotFired", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(hub, new object[] { hub.PlayerPosition + Vector3.up, source.transform.position, health });
                    health.TakeDamage(1f, WeaponRarity.Legendary);
                    Check(responders.Length >= 2 && responders.All(b => b.State == GullState.DistressDive), "A surviving simulated-shot hit triggers nearby group distress in Main");
                    Check(responders.Select((b, i) => b.CarriedFod == carried[i]).All(ok => ok), "Distress retains each responder's carried FOD");
                    Check(oldHolder == null || !responders.Contains(oldHolder) || kettle.TokenHolder != oldHolder, "An alerted token owner releases its bombing token");
                    distressTime = Time.time; stage = 3;
                }
                else if (stage == 3 && Time.time - distressTime > 4.2f)
                {
                    Check(responders.All(b => b.State != GullState.DistressDive && Mathf.Approximately(b.GetComponent<IBirdMotor>().SpeedMultiplier, 1f)),
                        "Main distress responders recover within four active seconds and reset speed");
                    if (!game.CompleteWave()) throw new InvalidOperationException("Could not complete fixture wave through GameManager.");
                    Check(FodManager.Instance.ActiveCount == 0 && FodManager.Instance.LandedCount == 0 && kettle.Members.Count == 0 && kettle.TokenHolder == null,
                        "WaveCompleted clears carried/falling/landed FOD, kettle membership and token");
                    Check(MainGullIntegration.InMain<FodSpawnPoint>().All(p => p.Claimant == null && p.CooldownRemaining == 0f) && game.CurrentState == GameState.Shopping,
                        "Supplies reset and existing between-wave shop state opens");
                    Report.Add("GameManager.TotalWaves=" + game.TotalWaves + ". Three-wave setup and production bird spawn/cleanup still belong to the future WaveManager milestone.");
                    Finish(true);
                }
            }
            catch (Exception error) { Check(false, error.ToString()); Finish(true); }
        }
        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(bool stopPlay)
        {
            running = false; EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            if (birds != null) foreach (var bird in birds) if (bird != null) bird.gameObject.SetActive(false);
            if (controller != null) controller.enabled = controllerWasEnabled;
            Report.Add("One focused Main run, not end-to-end weapon raycasts, all-species/door regressions, performance certification or a production three-wave playthrough. No Play-mode scene saved; fixture deactivates remaining birds as a future wave controller must.");
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines("Docs/IS/Gull/MainPlayValidation.txt", Report);
            Debug.Log("[MainGullCheck] Finished with " + failures + " failures; see MainPlayValidation.txt.");
            if (stopPlay) EditorApplication.isPlaying = false;
        }
    }
}
