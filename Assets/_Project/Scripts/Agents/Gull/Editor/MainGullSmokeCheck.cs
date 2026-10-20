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
        private const string OriginalWaveEnabledKey = "RunwayPanic.MainGullSmoke.WaveEnabled";
        private static readonly List<string> Report = new List<string>();
        private static bool running, dropped, settled, controllerWasEnabled, isolationChecked;
        private static int stage, failures;
        private static double began;
        private static float distressTime;
        private static GullBrain[] birds, responders;
        private static GullKettle kettle;
        private static PlayerController controller;
        private static PlayerHealth player;
        private static WaveManager pausedWaves;

        static MainGullSmokeCheck()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isPlaying || !SessionState.GetBool(Key, false) || scene.path != MainGullIntegration.MainPath) return;
            var waves = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<WaveManager>(true)).ToArray();
            if (waves.Length != 1) return; // EnteredPlayMode reports the missing/duplicate manager.
            PauseWaves(waves[0]); // Scene-loaded callbacks run before Start/Update starts the countdown.
        }

        private static void PauseWaves(WaveManager waves)
        {
            if (pausedWaves != null) return;
            pausedWaves = waves;
            waves.enabled = false;
        }
        [MenuItem("Tools/IS/Gull/Run Focused Main Gull Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainGullIntegration.MainPath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Open/save connected Main outside Play first.");
            var waves = MainGullIntegration.InMain<WaveManager>(true);
            if (waves.Length != 1 || !waves[0].enabled)
                throw new InvalidOperationException("Main needs exactly one enabled production WaveManager before this check.");
            MainGullIntegration.Validate(); SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && SessionState.GetBool(Key, false))
            {
                // The project disables scene reload, so pause before the first Play update.
                var waves = MainGullIntegration.InMain<WaveManager>(true).Single();
                SessionState.SetBool(OriginalWaveEnabledKey, waves.enabled);
                PauseWaves(waves);
                return;
            }
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                if (running) { Check(false, "Interrupted"); Finish(false); }
                return;
            }
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                if (SessionState.GetBool(Key, false))
                {
                    var waves = MainGullIntegration.InMain<WaveManager>(true);
                    if (waves.Length == 1) waves[0].enabled = SessionState.GetBool(OriginalWaveEnabledKey, true);
                }
                pausedWaves = null;
                SessionState.SetBool(Key, false);
                return;
            }
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                running = true; stage = failures = 0; dropped = settled = isolationChecked = false;
                began = EditorApplication.timeSinceStartup; controller = null; birds = responders = null; Report.Clear();
                Report.Add("Main Gull — focused real-scene check, Unity " + Application.unityVersion);
                Report.Add("Temporary birds use real Spawn markers. The production WaveManager is suspended for this controlled check; the fixture calls BeginWave/CompleteWave explicitly.");
                try
                {
                    if (pausedWaves == null) PauseWaves(MainGullIntegration.InMain<WaveManager>(true).Single());
                    bool ready = SessionState.GetBool(OriginalWaveEnabledKey, false) && !pausedWaves.enabled && pausedWaves.Phase == WaveManager.WavePhase.Waiting;
                    Check(ready, "Production WaveManager suspended before the test wave and countdown (phase=" + pausedWaves.Phase + ", enabled=" + pausedWaves.enabled + ")");
                    if (!ready) throw new InvalidOperationException("The production WaveManager was disabled or had already started; refusing to run the fixture.");
                }
                catch (Exception error) { Check(false, error.ToString()); Finish(true); return; }
                EditorApplication.update += Tick;
            }
        }
        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 70) throw new TimeoutException("Main check timed out at stage " + stage);
                if (EditorApplication.timeSinceStartup - began < 1) return;
                if (pausedWaves == null || pausedWaves.enabled) throw new InvalidOperationException("Production waves are not suspended; aborting controlled check.");
                if (!isolationChecked && stage > 0 && EditorApplication.timeSinceStartup - began >= 7)
                {
                    isolationChecked = true;
                    var health = MainGullIntegration.InMain<BirdHealth>();
                    Check(pausedWaves.Phase == WaveManager.WavePhase.Waiting && health.Length == 3 && birds.All(b => health.Contains(b.GetComponent<BirdHealth>())),
                        "After the normal six-second countdown, only the three fixture birds exist and automatic waves remain idle (phase=" + pausedWaves.Phase + ", birds=" + health.Length + ")");
                }
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
                    Check(isolationChecked, "Wave isolation observed beyond the normal production countdown");
                    Report.Add("GameManager.TotalWaves=" + game.TotalWaves + ". Production waves are present but were suspended for this fixture.");
                    Finish(true);
                }
            }
            catch (Exception error) { Check(false, error.ToString()); Finish(true); }
        }
        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(bool stopPlay)
        {
            running = false; EditorApplication.update -= Tick;
            if (birds != null) foreach (var bird in birds) if (bird != null) bird.gameObject.SetActive(false);
            if (controller != null) controller.enabled = controllerWasEnabled;
            Report.Add("One focused Main run, not end-to-end weapon raycasts, all-species/door regressions, performance certification or a production four-wave playthrough. No Play-mode scene saved; fixture deactivates its temporary birds.");
            Report.Add("Failures: " + failures);
            try
            {
                Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines("Docs/IS/Gull/MainPlayValidation.txt", Report);
                Debug.Log("[MainGullCheck] Finished with " + failures + " failures; see MainPlayValidation.txt.");
            }
            finally { if (stopPlay) EditorApplication.isPlaying = false; }
        }
    }
}
