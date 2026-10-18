using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RunwayPanic.Core;
using RunwayPanic.Player;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>Bounded Editor-only sampling of Unity's existing script markers; never fabricates CPU time from FPS.</summary>
    [InitializeOnLoad]
    public static class GullPerformanceCheck
    {
        private const string Key = "RunwayPanic.GullPerformance";
        private const string ReportPath = "Docs/IS/Gull/PerformanceSample.md";
        private const double WarmupSeconds = 15, CaptureSeconds = 15;
        private const int Capacity = 8192;
        private static readonly Regex Included = new Regex(@"(?:^|[ .:])(?:GullBrain|GullKettle|FodManager|FodItem|FodSpawnPoint|BirdMotor|BirdAnimatorDriver)\.(?:Update|FixedUpdate|LateUpdate|OnCollisionEnter)(?:\(|$)");
        private sealed class Capture { public string Name; public ProfilerRecorder Recorder; }
        private static readonly List<Capture> Captures = new List<Capture>();
        private static readonly List<string> Details = new List<string>();
        private static bool running, profilerWasEnabled, profilerChanged, controllerWasEnabled;
        private static int stage, lastFrame;
        private static double began, stageBegan;
        private static GullBrain[] birds;
        private static PlayerHealth player;
        private static PlayerController controller;

        static GullPerformanceCheck()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (running) Finish("Interrupted by script reload", false); };
        }
        [MenuItem("Tools/IS/Gull/Inspect Available Timing Markers")]
        public static void InspectMarkers()
        {
            var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/GullProfilerMarkers.txt", handles.Select(ProfilerRecorderHandle.GetDescription)
                .Where(d => d.Name.Contains("Gull") || d.Name.Contains("Fod") || d.Name.Contains("BirdMotor") || d.Name.Contains("BirdAnimator"))
                .Select(d => d.Name + " | " + d.Category + " | " + d.UnitType));
        }
        [MenuItem("Tools/IS/Gull/Measure Eight Gull Editor Performance")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != GullG1Setup.SandboxPath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save/open the Gull sandbox outside Play first.");
            if (ProfilerDriver.deepProfiling) throw new InvalidOperationException("Turn off Deep Profile before sampling.");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                running = true; stage = 0; lastFrame = -1; profilerChanged = false;
                began = stageBegan = EditorApplication.timeSinceStartup;
                birds = null; controller = null; player = null; Captures.Clear(); Details.Clear();
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running) Finish("Interrupted", false);
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
        }
        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - began > 65) throw new TimeoutException("Performance fixture exceeded its 65-second wall-clock limit.");
                if (stage == 0)
                {
                    if (now - began < 1) return;
                    if (UnityEngine.Object.FindObjectsByType<BirdHealth>(FindObjectsSortMode.None).Length != 0)
                        throw new InvalidOperationException("Start a fresh sandbox without pre-spawned birds.");
                    var supplies = UnityEngine.Object.FindObjectsByType<FodSpawnPoint>(FindObjectsSortMode.None).OrderBy(p => p.name).ToArray();
                    if (supplies.Length < 8) throw new InvalidOperationException("Expected at least eight real FOD supply points.");
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                    controller = player.GetComponent<PlayerController>(); controllerWasEnabled = controller.enabled; controller.enabled = false;
                    var capsule = player.GetComponent<CharacterController>();
                    capsule.enabled = false; player.transform.position = new Vector3(0, .12f, 0); capsule.enabled = true; player.ResetHealth();
                    BirdStateLabel.Visible = false;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullG1Setup.PrefabPath);
                    birds = new GullBrain[8];
                    for (int i = 0; i < birds.Length; i++)
                        birds[i] = UnityEngine.Object.Instantiate(prefab, supplies[i].ApproachPosition + Vector3.up * 2f, Quaternion.identity).GetComponent<GullBrain>();
                    profilerWasEnabled = Profiler.enabled; Profiler.enabled = true; profilerChanged = true;
                    stage = 1; stageBegan = now; WriteReport("Warming up eight autonomous Gulls");
                    return;
                }
                if (BirdRegistry.CountAlive(BirdSpecies.Gull) != 8 || birds.Any(b => b == null || !b.IsAlive || !b.enabled))
                    throw new InvalidOperationException("Eight live enabled Gulls must remain throughout the fixture.");
                if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                    throw new InvalidOperationException("Gameplay became inactive; measurement is inconclusive.");
                BirdStateLabel.Visible = false;
                if (GullDebugStats.Instance != null) GullDebugStats.Instance.Visible = false;
                if (player.CurrentHP < player.MaxHP * .25f) player.ResetHealth(); // Keep the stationary fixture alive; not a game-balance edit.
                if (stage == 1 && now - stageBegan >= WarmupSeconds && GullDebugStats.Instance != null && GullDebugStats.Instance.ResolvedDrops > 0)
                {
                    var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
                    var seen = new HashSet<string>();
                    foreach (var handle in handles)
                    {
                        var description = ProfilerRecorderHandle.GetDescription(handle);
                        if (!Included.IsMatch(description.Name) || description.UnitType.ToString() != "TimeNanoseconds" || !seen.Add(description.Name)) continue;
                        var recorder = new ProfilerRecorder(handle, Capacity, ProfilerRecorderOptions.StartImmediately |
                            ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                        if (!recorder.Valid) { recorder.Dispose(); continue; }
                        Captures.Add(new Capture { Name = description.Name, Recorder = recorder });
                    }
                    string[] required = { "GullBrain.Update", "GullKettle.Update", "FodManager.Update", "FodSpawnPoint.Update", "FodItem.Update", "FodItem.OnCollisionEnter", "BirdMotor.FixedUpdate", "BirdAnimatorDriver.Update", "BirdAnimatorDriver.LateUpdate" };
                    var missing = required.Where(name => !Captures.Any(c => c.Name.Contains(name))).ToArray();
                    if (missing.Length != 0)
                    {
                        Details.Add("Missing required script markers: " + string.Join(", ", missing));
                        Details.Add("Available matching descriptions: " + string.Join("; ", handles.Select(ProfilerRecorderHandle.GetDescription).Where(d => Included.IsMatch(d.Name)).Select(d => d.Name + " [" + d.UnitType + "]")));
                        throw new InvalidOperationException("Required Unity script timing markers unavailable; no performance verdict.");
                    }
                    Details.Add("Actual warmup: " + F(now - stageBegan) + " s, including at least one real FOD impact before recorder discovery.");
                    Details.Add("Warmup end states: " + string.Join(", ", birds.GroupBy(b => b.State).Select(g => g.Key + "=" + g.Count())));
                    stage = 2; stageBegan = now; WriteReport("Capturing 15 seconds of actual script timings");
                }
                else if (stage == 2 && now - stageBegan >= CaptureSeconds)
                {
                    foreach (var capture in Captures) capture.Recorder.Stop();
                    var brainSamples = Captures.First(c => c.Name.Contains("GullBrain.Update")).Recorder.ToArray().Where(s => s.Count > 0).ToArray();
                    int frames = brainSamples.Length;
                    if (brainSamples.Any(s => s.Count != 8)) throw new InvalidOperationException("A recorded brain frame did not contain exactly eight updates; no verdict.");
                    if (frames < 100 || Captures.Any(c => c.Recorder.WrappedAround || c.Recorder.Count >= Capacity))
                        throw new InvalidOperationException("Insufficient samples or full timing buffers; no verdict.");
                    double combinedTotal = 0, summedPeaks = 0, largestMarkerPeak = 0;
                    Details.Add("Brain-marker frame samples: " + frames + ". Recorded wall-clock interval: " + F(now - stageBegan) + " s.");
                    Details.Add(""); Details.Add("| Unity script marker | Calls | Non-empty samples | Mean contribution per brain frame (ms) | Non-empty sample p95 (ms) | Marker peak (ms) |");
                    Details.Add("|---|---:|---:|---:|---:|---:|");
                    foreach (var capture in Captures.OrderBy(c => c.Name))
                    {
                        var samples = capture.Recorder.ToArray().Where(s => s.Count > 0).ToArray();
                        var values = samples.Select(s => s.Value / 1000000d).OrderBy(v => v).ToArray();
                        double total = values.Sum(), peak = values.Length > 0 ? values[values.Length - 1] : 0;
                        combinedTotal += total; summedPeaks += peak;
                        largestMarkerPeak = Math.Max(largestMarkerPeak, peak);
                        Details.Add("| " + capture.Name + " | " + samples.Sum(s => (long)s.Count) + " | " + samples.Length + " | " + F(total / frames) + " | " +
                            F(values.Length > 0 ? values[Math.Max(0, (int)Math.Ceiling(values.Length * .95) - 1)] : 0) + " | " + F(peak) + " |");
                    }
                    Details.Add("");
                    Details.Add("Combined recorded scope mean: **" + F(combinedTotal / frames) + " ms per brain frame**. " + (combinedTotal / frames < .5 ? "Below" : "Not below") + " 0.5 ms on average in this Editor sample.");
                    Details.Add("Sum of individual marker peaks: " + F(summedPeaks) + " ms — a conservative bound, NOT a measured same-frame combined peak. Rows' p95 values are per marker/non-empty sample, NOT a combined-frame p95.");
                    Details.Add(largestMarkerPeak >= .5
                        ? "At least one individual script-marker peak reached/exceeded 0.5 ms, so the strict every-frame flock budget was NOT maintained in this capture. This does not identify the spike's cause."
                        : "No individual script-marker peak reached 0.5 ms; same-frame combined peaks still need a full trace before certifying an every-frame budget.");
                    Details.Add("End states: " + string.Join(", ", birds.GroupBy(b => b.State).Select(g => g.Key + "=" + g.Count())));
                    Finish("Completed eight-Gull Editor sample; scoped result only", true);
                }
            }
            catch (Exception error) { Finish("INCONCLUSIVE: " + error.Message, true); }
        }
        private static string F(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
        private static void WriteReport(string status)
        {
            var report = new List<string>
            {
                "# Gull — eight-bird Editor performance sample", "", "Status: " + status, "",
                "UTC: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ". Unity " + Application.unityVersion + "; " + SystemInfo.operatingSystem + "; " + SystemInfo.processorType + ".",
                "Editor Play mode, eight actual Gull prefabs spawned above eight existing FOD points; natural collection, formation and attack. Minimum 15 s warmup (also wait for a real FOD impact) + 15 s capture. No Deep Profile. Labels and Gull overlay hidden. State-transition logging is opt-in; inspect the GullBrain setting used for the run.",
                "Stationary player; controller disabled transiently and HP restored below 25% to avoid ending the fixture. No gun hits/distress stimulus. Saved scenes/prefabs/species/motor/animation code are unchanged.",
                "Unity ProfilerRecorder uses existing CPU script-message markers, SumAllSamplesInFrame, main thread only, capacity 8192. Nanoseconds converted to milliseconds. All recorder buffers start/stop together; combined mean = sum of captured marker durations / non-empty GullBrain.Update frame sample count. Every included brain sample must contain exactly eight calls; missing/partial frames cannot be treated as cheaper eight-Gull work. This is not FPS or whole-frame CPU time.",
                "Scope: recorded Gull brain/kettle/FOD/supply callbacks plus shared BirdMotor physics updates and BirdAnimatorDriver updates for these eight Gulls. Synchronous called work is included inside those callbacks. Rendering/GPU, animation-system jobs, general engine physics, debug labels/stats, player/hub scripts and other unlisted callbacks are excluded. Missing/never-called callbacks have no invented samples; consult the actual marker rows.",
                "The 0.5 ms target is compared only to the scoped mean. This Editor sample does NOT certify a standalone development build, every-frame target, distress/spawn/death worst cases, or the whole game's script cost.", ""
            };
            report.AddRange(Details);
            report.Add(""); report.Add("API reference: [Unity ProfilerRecorderOptions](https://docs.unity.com/en-us/engine/6000.7/script-reference/unity/profiling/profilerrecorderoptions). SumAllSamplesInFrame aggregates repeated calls; do not divide the eight-Gull sum by eight when comparing a per-frame flock budget.");
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines(ReportPath, report);
        }
        private static void Finish(string status, bool stopPlay)
        {
            running = false; EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            try { WriteReport(status); }
            finally
            {
                foreach (var capture in Captures) capture.Recorder.Dispose(); Captures.Clear();
                if (profilerChanged) { Profiler.enabled = profilerWasEnabled; profilerChanged = false; }
                if (controller != null) controller.enabled = controllerWasEnabled;
                if (birds != null) foreach (var bird in birds) if (bird != null) bird.gameObject.SetActive(false);
                Debug.Log("[GullPerformance] " + status + "; see " + ReportPath);
                if (stopPlay) EditorApplication.isPlaying = false;
            }
        }
    }
}
