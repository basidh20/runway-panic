using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>Opt-in, short sandbox-only G1 check. Does not run other birds or the AI/stress suites.</summary>
    [InitializeOnLoad]
    public static class GullG1SmokeCheck
    {
        private const string SessionKey = "RunwayPanic.GullG1Smoke";
        private static readonly List<string> Report = new List<string>();
        private static readonly HashSet<GullState> Seen = new HashSet<GullState>();
        private static GullBrain[] birds;
        private static bool running;
        private static int failures, shopStage;
        private static double began, shopBegan;
        private static GullState frozenState;
        private static float frozenTime;

        static GullG1SmokeCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Tools/IS/Gull/Run Focused G1 Check")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != GullG1Setup.SandboxPath || scene.isDirty)
                throw new InvalidOperationException("Open and save the Gull sandbox outside Play first.");
            SessionState.SetBool(SessionKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                failures = shopStage = 0; birds = null; Seen.Clear(); Report.Clear(); running = true;
                began = EditorApplication.timeSinceStartup;
                Report.Add("Gull G1 — one focused sandbox state-preview/config/shop-freeze/death check");
                Report.Add("Unity: " + Application.unityVersion);
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running)
            { Check(false, "Check interrupted"); Finish(false); }
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(SessionKey, false);
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 40) throw new TimeoutException("G1 check exceeded 40 seconds.");
                if (EditorApplication.timeSinceStartup - began < 1) return;
                if (birds == null)
                {
                    UnityEngine.Object.FindObjectsByType<BirdTestSpawner>(FindObjectsSortMode.None).Single().Spawn();
                    birds = UnityEngine.Object.FindObjectsByType<GullBrain>(FindObjectsSortMode.None);
                    Check(birds.Length == 5 && birds.All(bird => bird.GetComponents<BirdBrain>().Length == 1 &&
                        bird.GetComponent<BirdHealth>().Species.Species == BirdSpecies.Gull), "Five Gull instances, one species brain each");
                    Check(birds.All(bird => bird.Config != null && bird.Config == birds[0].Config), "All five share the assigned Gull strategy asset");
                    Check(birds.All(bird => bird.GetComponent<BirdStateLabel>() != null &&
                        ReferenceEquals(bird.CurrentStateName, bird.CurrentStateName)), "Shared state labels present; state names are cached strings");
                    return; // Let the normal Start/Update lifecycle run.
                }
                if (birds.Length != 5 || birds.Any(bird => bird == null || !bird.enabled))
                    throw new InvalidOperationException("Expected five living, enabled G1 birds.");
                Seen.Add(birds[0].State);
                if (shopStage == 0 && birds[0].State == GullState.GatherFod)
                {
                    Check(GameManager.Instance != null && GameManager.Instance.OpenShop(), "Sandbox can enter the shared between-wave shop state");
                    frozenState = birds[0].State; frozenTime = birds[0].StateTime;
                    shopBegan = EditorApplication.timeSinceStartup; shopStage = 1;
                }
                if (shopStage == 1)
                {
                    if (EditorApplication.timeSinceStartup - shopBegan < 2.2) return;
                    Check(birds[0].State == frozenState && Mathf.Abs(birds[0].StateTime - frozenTime) < .001f,
                        "State and timer freeze throughout 2.2 seconds in Shopping");
                    Check(GameManager.Instance.CloseShop(), "Closing the shop resumes Playing");
                    shopStage = 2;
                }
                if (shopStage != 2 || Seen.Count != 9 || birds[0].State != GullState.DistressDive) return;
                Check(birds.All(bird => bird.State == GullState.DistressDive), "All five cycle through the nine living preview states; Dead is never entered by the timer");
                Report.Add("Observed: " + string.Join(", ", Seen.OrderBy(state => state)));
                var health = birds[0].GetComponent<BirdHealth>();
                health.Kill();
                Check(birds[0].State == GullState.Dead && !birds[0].enabled && !health.IsAlive,
                    "Actual shared health death enters Dead and disables the brain");
                Finish(true);
            }
            catch (Exception error) { Check(false, error.Message); Finish(true); }
        }

        private static void Check(bool ok, string message)
        {
            Report.Add((ok ? "PASS: " : "FAIL: ") + message);
            if (!ok) failures++;
        }

        private static void Finish(bool stopPlay)
        {
            running = false;
            EditorApplication.update -= Tick;
            SessionState.SetBool(SessionKey, false);
            Report.Add("G1 only: no FOD, kettle/token, prediction, distress reaction or production waves tested or implemented.");
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull");
            File.WriteAllLines("Docs/IS/Gull/G1Validation.txt", Report);
            Debug.Log("[GullG1Check] Finished with " + failures + " failures; see Docs/IS/Gull/G1Validation.txt.");
            if (stopPlay) EditorApplication.isPlaying = false;
        }
    }
}
