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
    /// <summary>One sandbox formation/token check with actual spawned Gulls and the shared motor.</summary>
    [InitializeOnLoad]
    public static class GullG3SmokeCheck
    {
        private const string Key = "RunwayPanic.GullG3Smoke";
        private static readonly List<string> Report = new List<string>();
        private static bool running;
        private static int stage, failures;
        private static double began, stageBegan, killedAt;
        private static GullKettle kettle;
        private static GullBrain[] birds;
        private static GullBrain originalHolder, killedHolder;
        private static Vector3[] beforeMovement;
        private static float beforeRotation;

        static GullG3SmokeCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }
        [MenuItem("Tools/IS/Gull/Run Focused G3 Kettle Check")]
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
                failures = stage = 0; running = true; began = EditorApplication.timeSinceStartup;
                kettle = null; birds = null; originalHolder = killedHolder = null; Report.Clear();
                Report.Add("Gull G3 — focused formation/token sandbox check, Unity " + Application.unityVersion);
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
                if (EditorApplication.timeSinceStartup - began > 70)
                {
                    if (birds != null) foreach (var bird in birds.Where(bird => bird != null))
                        Report.Add("INFO: " + bird.State + " at " + bird.transform.position + ", slot distance=" + Vector3.Distance(bird.transform.position, kettle.GetSlotPosition(bird)).ToString("F2"));
                    throw new TimeoutException("Kettle check exceeded 70 seconds at stage " + stage);
                }
                if (EditorApplication.timeSinceStartup - began < 1) return;
                var game = GameManager.Instance;
                if (stage == 0)
                {
                    kettle = GullKettle.Instance;
                    kettle.Configure(AssetDatabase.LoadAssetAtPath<GullStrategyConfig>(GullG1Setup.ConfigPath));
                    kettle.DispatchEnabled = false; // First observe all five orbiting before letting a turn depart.
                    UnityEngine.Object.FindObjectsByType<BirdTestSpawner>(FindObjectsSortMode.None).Single().Spawn();
                    birds = UnityEngine.Object.FindObjectsByType<GullBrain>(FindObjectsSortMode.None);
                    Check(birds.Length == 5 && kettle.Centre == new Vector3(0, 10, 0) && kettle.GroundHeight == 0, "Five test Gulls and ground-relative runway kettle anchor");
                    Next(1);
                }
                else if (stage == 1 && kettle.Members.Count == 5 && birds.All(bird => bird.State == GullState.InKettle && bird.CarriedFod != null) &&
                    birds.All(bird => Vector3.Distance(bird.transform.position, kettle.GetSlotPosition(bird)) < 4.5f))
                {
                    var slots = birds.Select(bird => kettle.GetSlotPosition(bird)).ToArray();
                    Check(slots.Select(point => Mathf.RoundToInt(point.y)).OrderBy(height => height).SequenceEqual(new[] { 6, 8, 10, 12, 14 }) &&
                        slots.All(point => Mathf.Abs(new Vector2(point.x, point.z).magnitude - 8) < .01f),
                        "Five unique spiral slots have radius 8 and ground heights 6/8/10/12/14 m");
                    Check(birds.All(bird => bird.CarriedFod.transform.parent == bird.transform), "Five actual Gulls join and follow their slots carrying distinct debris");
                    beforeRotation = kettle.Rotation; beforeMovement = birds.Select(bird => bird.transform.position).ToArray(); Next(2);
                }
                else if (stage == 2 && EditorApplication.timeSinceStartup - stageBegan > 1)
                {
                    Check(Mathf.DeltaAngle(beforeRotation, kettle.Rotation) > 15 && birds.Where((bird, i) => Vector3.Distance(bird.transform.position, beforeMovement[i]) > .2f).Count() == 5,
                        "Rotation advances and every bird moves through the shared motor");
                    if (!game.OpenShop()) throw new InvalidOperationException("Could not enter Shopping.");
                    beforeRotation = kettle.Rotation; Next(3);
                }
                else if (stage == 3 && EditorApplication.timeSinceStartup - stageBegan > .5)
                {
                    Check(Mathf.Abs(Mathf.DeltaAngle(beforeRotation, kettle.Rotation)) < .001f && kettle.TokenHolder == null, "Formation scheduling freezes while Shopping");
                    game.CloseShop(); kettle.DispatchEnabled = true; Next(4);
                }
                else if (stage == 4 && kettle.TokenHolder != null)
                {
                    originalHolder = kettle.TokenHolder;
                    var other = birds.First(bird => bird != originalHolder);
                    Check(birds.Count(IsAttacking) == 1 && !kettle.TakeToken(other) && !kettle.ReturnToken(other),
                        "Exactly one departure; other Gulls cannot take or return its token");
                    Check(originalHolder == kettle.Members[kettle.Members.Count - 1], "Only the logical bottom member obtains the drop token");
                    Next(5);
                }
                else if (stage == 5 && kettle.TokenHolder != null && kettle.TokenHolder != originalHolder)
                {
                    Check(kettle.Members[0] == originalHolder,
                        "A completed turn returns its holder to the top and gives the next bottom Gull a turn");
                    killedHolder = kettle.TokenHolder;
                    killedHolder.GetComponent<BirdHealth>().Kill(); killedAt = EditorApplication.timeSinceStartup;
                    Check(kettle.Members.Count == 4 && kettle.TokenHolder != killedHolder, "Actual holder death immediately removes membership and releases ownership"); Next(6);
                }
                else if (stage == 6 && kettle.TokenHolder != null)
                {
                    Check(EditorApplication.timeSinceStartup - killedAt < 1 && kettle.TokenHolder != killedHolder,
                        "The next living bottom Gull takes the token within one second of holder death");
                    Check(birds.Where(bird => bird != null && bird.IsAlive).Count(IsAttacking) == 1,
                        "Only one living Gull remains in departure after death recovery");
                    if (!game.BeginWave(1) || !game.CompleteWave()) throw new InvalidOperationException("Could not exercise WaveCompleted.");
                    Check(kettle.Members.Count == 0 && kettle.TokenHolder == null && kettle.Rotation == 0, "WaveCompleted clears kettle members, token and rotation");
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
            if (kettle != null) kettle.Clear();
            Report.Add("Current G4 attacks may complete turns before the 12 s timeout. This check observes turn return/death recovery; the original G3 report recorded the timeout separately. No production wave or stress suite run.");
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines("Docs/IS/Gull/G3Validation.txt", Report);
            Debug.Log("[GullG3Check] Finished with " + failures + " failures; see G3Validation.txt.");
            if (stopPlay) EditorApplication.isPlaying = false;
        }
        private static bool IsAttacking(GullBrain bird) => bird.State == GullState.DescendToDrop || bird.State == GullState.Telegraph || bird.State == GullState.Drop;
    }
}
