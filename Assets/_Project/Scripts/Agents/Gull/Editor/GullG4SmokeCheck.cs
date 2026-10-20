using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>One focused run: lead maths plus real stationary/walking/dodging drops and no-FOD fallback.</summary>
    [InitializeOnLoad]
    public static class GullG4SmokeCheck
    {
        private const string Key = "RunwayPanic.GullG4Smoke";
        private static readonly List<string> Report = new List<string>();
        private static bool running, warned, dropped, settled, hit, locked, sawSwoop, alignmentLogged;
        private static int failures, caseIndex, lastFrame, impacts;
        private static double began, caseBegan;
        private static float warningTime, releaseDelay;
        private static GullBrain bird;
        private static PlayerHealth player;
        private static PlayerController controller;
        private static CharacterController capsule;
        private static Vector3 warningPoint, warningPlayer, warningVelocity, impactPosition, releasePosition, dodgeVelocity;

        static GullG4SmokeCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }
        [MenuItem("Tools/IS/Gull/Run Focused G4 Bombing Check")]
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
                running = true; failures = 0; caseIndex = -1; lastFrame = -1; began = EditorApplication.timeSinceStartup;
                bird = null; Report.Clear(); Report.Add("Gull G4 — focused maths/real bombing sandbox check, Unity " + Application.unityVersion);
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running) { Check(false, "Check interrupted"); Finish(false); }
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
        }
        private static void StartCase(int index)
        {
            if (bird != null) { bird.gameObject.SetActive(false); UnityEngine.Object.Destroy(bird.gameObject); }
            FodManager.Instance.ClearAll(); GullKettle.Instance.Clear();
            foreach (var supply in UnityEngine.Object.FindObjectsByType<FodSpawnPoint>(FindObjectsSortMode.None))
            { supply.enabled = index != 3; supply.ResetForWave(); }
            capsule.enabled = false; player.transform.position = new Vector3(0, .12f, 0); capsule.enabled = true;
            player.ResetHealth();
            caseIndex = index; caseBegan = EditorApplication.timeSinceStartup;
            warned = dropped = settled = hit = sawSwoop = false; locked = true; impacts = 0;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullG1Setup.PrefabPath);
            bird = UnityEngine.Object.Instantiate(prefab, index == 3 ? new Vector3(8, 12, 0) : new Vector3(-8.5f, 3, -10), Quaternion.identity).GetComponent<GullBrain>();
            bird.TelegraphStarted += source =>
            {
                warned = true; warningTime = Time.time; warningPoint = source.PredictedDropPoint;
                warningPlayer = PlayerEventHub.Instance.PlayerPosition; warningVelocity = PlayerEventHub.Instance.PlayerVelocity;
                Vector3 horizontal = new Vector3(warningVelocity.x, 0, warningVelocity.z);
                dodgeVelocity = horizontal.sqrMagnitude > .01f ? Quaternion.Euler(0, 90, 0) * horizontal.normalized * 4.5f : Vector3.right * 4.5f;
                Report.Add("INFO: case " + caseIndex + " warning at bird=" + source.transform.position.ToString("F2") + ", aim=" + warningPoint.ToString("F2"));
                alignmentLogged = false;
                File.WriteAllLines("Docs/IS/Gull/G4Validation.txt", Report.Concat(new[] { "RUNNING" }));
            };
            bird.FodDropped += (item, target) =>
            {
                dropped = true; releaseDelay = Time.time - warningTime; releasePosition = item.transform.position;
                item.Impacted += (piece, wasHit) => { impacts++; hit |= wasHit; };
                item.Settled += piece => { settled = true; impactPosition = piece.transform.position; };
            };
        }
        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup - began < 1) return;
                if (caseIndex < 0)
                {
                    Vector3 origin = new Vector3(1, 2, 3);
                    float lead = Mathf.Sqrt(14f / 9.81f) + .6f;
                    Check(GullTargeting.PredictDropPoint(origin, Vector3.zero, 7, .6f, 9.81f) == origin, "Stationary target prediction stays at the player's position");
                    Check(Vector3.Distance(GullTargeting.PredictDropPoint(origin, new Vector3(2, 100, 0), 7, .6f, 9.81f), origin + Vector3.right * (2 * lead)) < .001f,
                        "Lead includes warning plus fall time and ignores vertical player velocity");
                    Check(Mathf.Abs(Vector3.Distance(origin, GullTargeting.PredictDropPoint(origin, Vector3.one * 100, 7, .6f, 9.81f)) - 8) < .001f &&
                        !float.IsNaN(GullTargeting.PredictDropPoint(origin, Vector3.right, -1, -1, 0).x), "Lead clamps to eight metres and degenerate tuning stays finite");
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                    capsule = player.GetComponent<CharacterController>(); controller = player.GetComponent<PlayerController>(); controller.enabled = false;
                    StartCase(0); return;
                }
                if (EditorApplication.timeSinceStartup - caseBegan > 38)
                    throw new TimeoutException("Case " + caseIndex + " timed out: state=" + bird.State + ", bird=" + bird.transform.position + ", target=" + bird.PredictedDropPoint + ", dropped=" + dropped);
                if (caseIndex == 1 || caseIndex == 2)
                {
                    bool attacking = bird.State == GullState.DescendToDrop || bird.State == GullState.Telegraph || bird.State == GullState.Drop || dropped;
                    if (attacking) capsule.Move((caseIndex == 2 && warned ? dodgeVelocity : Vector3.forward * 2f) * Time.deltaTime);
                }
                if (warned && (bird.State == GullState.Telegraph || bird.State == GullState.Drop))
                    locked &= Vector3.Distance(warningPoint, bird.PredictedDropPoint) < .001f;
                if (bird.State == GullState.Drop && bird.StateTime > 1 && !alignmentLogged)
                {
                    alignmentLogged = true;
                    Report.Add("INFO: late alignment at bird=" + bird.transform.position.ToString("F2") + ", aim=" + bird.PredictedDropPoint.ToString("F2"));
                    File.WriteAllLines("Docs/IS/Gull/G4Validation.txt", Report.Concat(new[] { "RUNNING" }));
                }
                if (caseIndex < 3 && settled)
                {
                    Check(warned && releaseDelay >= .59f && locked && impacts == 1,
                        "Case " + caseIndex + ": one real release/impact after the warning; committed aim remains fixed (delay " + releaseDelay.ToString("F2") + " s)");
                    float targetError = new Vector2(impactPosition.x - warningPoint.x, impactPosition.z - warningPoint.z).magnitude;
                    Report.Add("MEASURE: case " + caseIndex + ", landing-to-aim=" + targetError.ToString("F2") + " m, HP loss=" + (100-player.CurrentHP).ToString("F1") +
                        ", release=" + releasePosition.ToString("F2") + ", aim=" + warningPoint.ToString("F2"));
                    if (caseIndex == 0) Check(targetError <= 1f && hit && Mathf.Abs(player.CurrentHP - 94) < .01f, "Stationary player: debris lands within one metre of aim and applies one six-HP hit");
                    else if (caseIndex == 1) Check(Vector3.Dot(warningPoint - warningPlayer, new Vector3(warningVelocity.x,0,warningVelocity.z)) > .5f && targetError <= 1.5f,
                        "Straight walking: the real drop lands ahead of the player position sampled at the warning");
                    else Check(!hit && player.CurrentHP == 100 && targetError <= 1.5f, "Turning during the warning dodges the committed bomb without it retargeting");
                    Check(GullKettle.Instance.TokenHolder != bird && bird.CarriedFod == null, "Case " + caseIndex + ": release clears carried FOD and returns the token");
                    StartCase(caseIndex + 1);
                }
                else if (caseIndex == 3)
                {
                    sawSwoop |= bird.IsSwooping && bird.State == GullState.Drop;
                    if (sawSwoop && bird.State == GullState.ClimbToTop)
                    {
                        Check(warned && !dropped && bird.CarriedFod == null, "No supply: a warned swoop is used instead of fabricating debris");
                        Check(GullKettle.Instance.TokenHolder != bird && player.CurrentHP >= 94, "Fallback is one bounded pass, returns the token and cannot repeat damage in the same turn");
                        Report.Add("MEASURE: fallback HP loss=" + (100-player.CurrentHP).ToString("F1")); Finish(true);
                    }
                }
            }
            catch (Exception error) { Check(false, error.Message); Finish(true); }
        }
        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(bool stopPlay)
        {
            running = false; EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            if (bird != null) bird.gameObject.SetActive(false);
            if (controller != null) controller.enabled = true;
            Report.Add("One focused scripted sandbox run, one drop per scenario. Not statistical balance/performance evidence. The real player was moved only transiently for these checks.");
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines("Docs/IS/Gull/G4Validation.txt", Report);
            Debug.Log("[GullG4Check] Finished with " + failures + " failures; see G4Validation.txt.");
            if (stopPlay) EditorApplication.isPlaying = false;
        }
    }
}
