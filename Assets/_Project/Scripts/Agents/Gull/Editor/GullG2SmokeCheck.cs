using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.AI;
using RunwayPanic.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    [InitializeOnLoad]
    public static class GullG2SmokeCheck
    {
        private const string Key = "RunwayPanic.GullG2Smoke";
        private static readonly List<string> Report = new List<string>();
        private static readonly List<FodItem> Landings = new List<FodItem>();
        private static bool running;
        private static int stage, failures;
        private static double began, stageBegan;
        private static FodManager manager;
        private static FodSpawnPoint[] supplies;
        private static GullBrain[] birds;
        private static GullBrain emptyBird;
        private static GullStrategyConfig capConfig, lifetimeConfig;
        private static FodItem timed;
        private static GameObject fodPrefab;
        private static float frozenCooldown;
        private static bool timedLanded;

        static GullG2SmokeCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }
        [MenuItem("Tools/IS/Gull/Run Focused G2 Collection Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != GullG1Setup.SandboxPath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Open/save the Gull sandbox outside Play first.");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                stage = failures = 0; running = true; began = stageBegan = EditorApplication.timeSinceStartup;
                manager = null; timed = null; capConfig = lifetimeConfig = null; timedLanded = false;
                Report.Clear(); Landings.Clear();
                Report.Add("Gull G2b — focused collection/supply/manager runtime check, Unity " + Application.unityVersion);
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running) { Check(false, "Check interrupted"); Finish(false); }
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
        }
        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 55)
                {
                    if (birds != null) foreach (var bird in birds) Report.Add("INFO: " + bird.State + " at " + bird.transform.position + "; carrying=" + (bird.CarriedFod != null));
                    throw new TimeoutException("G2 check exceeded 55 seconds at stage " + stage);
                }
                if (EditorApplication.timeSinceStartup - began < 1) return;
                var game = GameManager.Instance;
                if (stage == 0)
                {
                    manager = FodManager.Instance;
                    supplies = UnityEngine.Object.FindObjectsByType<FodSpawnPoint>(FindObjectsSortMode.None);
                    Check(supplies.Length == 8 && supplies.All(point => point.IsReady) && manager.Config.maxFodOnGround == 20,
                        "Eight ready supplies and one configured manager with the normal cap of 20");
                    Check(supplies[0].TryTake(out var probe) && !supplies[0].TryTake(out _), "One successful take starts the six-second cooldown; immediate repeat is refused");
                    if (probe != null) probe.Despawn();
                    UnityEngine.Object.FindObjectsByType<BirdTestSpawner>(FindObjectsSortMode.None).Single().Spawn();
                    birds = UnityEngine.Object.FindObjectsByType<GullBrain>(FindObjectsSortMode.None);
                    Check(birds.Length == 5 && supplies[1].TryClaim(birds[0]) && !supplies[1].TryClaim(birds[1]), "A supply accepts one Gull claim and refuses a competing Gull");
                    supplies[1].ReleaseClaim(birds[0]);
                    if (!game.OpenShop()) throw new InvalidOperationException("Could not enter Shopping for the cooldown check.");
                    frozenCooldown = supplies[0].CooldownRemaining;
                    Next(1);
                }
                else if (stage == 1 && EditorApplication.timeSinceStartup - stageBegan > .5)
                {
                    Check(Mathf.Abs(supplies[0].CooldownRemaining - frozenCooldown) < .001f, "Supply cooldown freezes while Shopping");
                    game.CloseShop(); Next(2);
                }
                else if (stage == 2 && birds.All(bird => bird.CarriedFod != null && bird.State == GullState.JoinKettle))
                {
                    Check(birds.All(bird => bird.CarriedFod.transform.parent == bird.transform) && birds.Select(bird => bird.CarriedFod).Distinct().Count() == 5,
                        "Five Gulls fly from airport spawns and collect distinct pieces, attached to their own roots");
                    Check(supplies.All(point => point.Claimant == null), "Completed pickups release every supply claim");
                    capConfig = UnityEngine.Object.Instantiate(manager.Config); capConfig.maxFodOnGround = 2;
                    manager.Configure(capConfig);
                    fodPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullFodSetup.PrefabPath);
                    foreach (float y in new[] { .5f, .9f, 1.3f })
                    {
                        var item = UnityEngine.Object.Instantiate(fodPrefab, new Vector3(2, y, -5), Quaternion.identity).GetComponent<FodItem>();
                        item.Settled += settled => Landings.Add(settled); item.Release();
                    }
                    Next(3);
                }
                else if (stage == 3 && Landings.Count == 3 && Landings[0] == null)
                {
                    Check(manager.LandedCount == 2 && Landings[1] != null && Landings[2] != null,
                        "Transient cap of two removes the oldest landed piece while retaining the two newest");
                    lifetimeConfig = UnityEngine.Object.Instantiate(capConfig); lifetimeConfig.fodLifetime = .25f;
                    timed = UnityEngine.Object.Instantiate(fodPrefab, new Vector3(2, 2, -9), Quaternion.identity).GetComponent<FodItem>();
                    timed.Configure(lifetimeConfig, AssetDatabase.LoadAssetAtPath<RunwayPanic.Data.BirdSpeciesData>("Assets/_Project/ScriptableObjects/Birds/BirdSpecies_Gull.asset"));
                    timed.Settled += item => timedLanded = true; timed.Release(); Next(4);
                }
                else if (stage == 4 && timed == null)
                {
                    var grid = FlightGrid.Instance;
                    Check(timedLanded && !grid.IsBlocked(grid.ClampToGrid(new Vector3(2, .8f, -9))), "Manager expires a landed piece and clears its grid block using a transient 0.25-second lifetime");
                    var falling = UnityEngine.Object.Instantiate(fodPrefab, new Vector3(3, 7, 0), Quaternion.identity).GetComponent<FodItem>();
                    falling.Release();
                    Check(manager.ActiveCount >= 6 && birds.All(bird => bird.CarriedFod != null), "Manager tracks carried and falling pieces as well as landed debris");
                    if (!game.BeginWave(1) || !game.CompleteWave()) throw new InvalidOperationException("Could not exercise WaveCompleted.");
                    Check(manager.ActiveCount == 0 && manager.LandedCount == 0 && supplies.All(point => point.CooldownRemaining == 0 && point.Claimant == null),
                        "WaveCompleted clears all item states and resets supply cooldowns/claims");
                    Next(5);
                }
                else if (stage == 5 && EditorApplication.timeSinceStartup - stageBegan > .2)
                {
                    Check(UnityEngine.Object.FindObjectsByType<FodItem>(FindObjectsSortMode.None).Length == 0 &&
                        !FlightGrid.Instance.IsBlocked(FlightGrid.Instance.ClampToGrid(new Vector3(2, .8f, -5))),
                        "No debris survives into the shop; the former landed cell is clear");
                    foreach (var supply in supplies) supply.enabled = false;
                    game.CloseShop();
                    emptyBird = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GullG1Setup.PrefabPath), new Vector3(0, 5, 0), Quaternion.identity).GetComponent<GullBrain>();
                    Next(6);
                }
                else if (stage == 6 && EditorApplication.timeSinceStartup - stageBegan > 3.3)
                {
                    Check(emptyBird.State == GullState.JoinKettle && emptyBird.CarriedFod == null, "With no usable supply, a Gull proceeds without FOD after three gameplay seconds");
                    Finish(true);
                }
            }
            catch (Exception error) { Check(false, error.Message); Finish(true); }
        }
        private static void Next(int next) { stage = next; stageBegan = EditorApplication.timeSinceStartup; }
        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(bool stopPlay)
        {
            running = false; EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            if (manager != null) manager.ClearAll();
            if (capConfig != null) UnityEngine.Object.Destroy(capConfig);
            if (lifetimeConfig != null) UnityEngine.Object.Destroy(lifetimeConfig);
            Report.Add("Sandbox/transient changes only; no shared config values changed. G3 formation/token and production waves remain future work.");
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines("Docs/IS/Gull/G2bValidation.txt", Report);
            Debug.Log("[GullG2Check] Finished with " + failures + " failures; see G2bValidation.txt.");
            if (stopPlay) EditorApplication.isPlaying = false;
        }
    }
}
