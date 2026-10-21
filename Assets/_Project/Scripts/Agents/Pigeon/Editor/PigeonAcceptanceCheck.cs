using System;
using System.Collections.Generic;
using System.IO;
using RunwayPanic.Core;
using RunwayPanic.Player;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RunwayPanic.Agents.Pigeon.EditorTools
{
    /// <summary>Runs the real pigeon prefab/motor in the pigeon sandbox, never saves a production asset.</summary>
    [InitializeOnLoad]
    public static class PigeonAcceptanceCheck
    {
        private const string Key = "RunwayPanic.PigeonAcceptance";
        private const string BatchKey = Key + ".Batch";
        private static readonly List<string> Results = new List<string>();
        private static readonly List<PigeonBrain> Birds = new List<PigeonBrain>();
        private static readonly List<Vector3> Starts = new List<Vector3>();
        private static int stage, failures, errors, editorIssues, samples, lastSampleFrame;
        private static float began, checkpoint;
        private static double wallStart, meanNs, peakNs;
        private static Vector3 shopPosition;
        private static float shopModeTime;
        private static double pauseBegan;
        private static PlayerHealth player;
        private static ProfilerRecorder brainRecorder, flockRecorder;

        static PigeonAcceptanceCheck() { EditorApplication.playModeStateChanged += OnMode; }

        [MenuItem("Tools/IS/Pigeon/Run Acceptance Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before running the pigeon check.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save or discard the current scene before opening the pigeon sandbox.");
            EditorSceneManager.OpenScene(PigeonSandboxSetup.SandboxPath);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        public static void RunBatch()
        {
            SessionState.SetBool(BatchKey, true);
            Run();
        }

        private static void OnMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                Results.Clear(); Birds.Clear(); Starts.Clear();
                stage = failures = errors = editorIssues = samples = 0;
                lastSampleFrame = -1;
                meanNs = peakNs = 0;
                began = Time.time; wallStart = EditorApplication.timeSinceStartup;
                Results.Add("# Pigeon acceptance check\n\nUnity " + Application.unityVersion + ". Real pigeon sandbox, prefab and shared motor. No scenes saved.");
                Application.logMessageReceived += Log;
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Key, false);
                if (SessionState.GetBool(BatchKey, false))
                {
                    SessionState.SetBool(BatchKey, false);
                    EditorApplication.Exit(failures == 0 ? 0 : 1);
                }
            }
        }

        private static void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                // Preserve this known Editor infrastructure failure separately; it has no game-code frames.
                // Every other error still fails the fixture, including errors from shared game dependencies.
                if (trace.Contains("UnityEditor.Search.SearchDatabase") && !trace.Contains("RunwayPanic."))
                { editorIssues++; Results.Add("Editor infrastructure issue (excluded from runtime result): " + message + "\n```\n" + trace + "\n```"); }
                else { errors++; Results.Add("Runtime error: " + message + "\n```\n" + trace + "\n```"); }
            }
        }

        private static void Check(bool passed, string message)
        {
            if (!passed) failures++;
            Results.Add("- " + (passed ? "PASS: " : "FAIL: ") + message);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup - wallStart > 180) throw new TimeoutException("Pigeon fixture exceeded 180 seconds.");
                if (Time.time - began < 2f) return;
                if (stage == 0)
                {
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                    if (player == null) throw new InvalidOperationException("Pigeon sandbox has no player.");
                    var controller = player.GetComponent<PlayerController>();
                    if (controller != null) controller.enabled = false;
                    var body = player.GetComponent<CharacterController>();
                    if (body != null) body.enabled = false;
                    player.transform.position = new Vector3(0f, 0.15f, 0f);
                    if (body != null) body.enabled = true;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PigeonSandboxSetup.PrefabPath);
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = i * Mathf.PI / 3f;
                        Vector3 position = player.transform.position + new Vector3(Mathf.Cos(angle) * 10f, 8f, Mathf.Sin(angle) * 10f);
                        var bird = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<PigeonBrain>();
                        Birds.Add(bird); Starts.Add(position);
                    }
                    brainRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "PigeonBrain.Update", 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
                    flockRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "PigeonFlock.Strategy", 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
                    checkpoint = Time.time; stage = 1;
                    return;
                }
                player.Heal(player.MaxHP);
                PigeonFlock flock = PigeonFlock.Instance;
                if (stage == 1)
                {
                    if (flock.Config == null) return;
                    if (brainRecorder.Valid && flockRecorder.Valid && Time.time - checkpoint > 5f && lastSampleFrame != Time.frameCount)
                    {
                        lastSampleFrame = Time.frameCount;
                        double ns = brainRecorder.LastValue + flockRecorder.LastValue;
                        meanNs += ns; peakNs = Math.Max(peakNs, ns); samples++;
                    }
                    CheckTokenLimit(flock);
                    if (Time.time - checkpoint < 35f) return;
                    Check(flock.Count == 6, "Six spawned pigeons join one flock");
                    int moved = 0;
                    for (int i = 0; i < Birds.Count; i++)
                        if (Vector3.Distance(Birds[i].Position, Starts[i]) > 2f) moved++;
                    Check(moved == 6, "All six pigeons move more than two metres");
                    Check(PigeonStats.RallySelections > 0, "Reachable rally candidates selected through shared search");
                    Check(PigeonStats.Dives > 0, "Flock dispatches attacks");
                    Check(PigeonStats.Hits > 0, "Real contact attack damages the player");
                    for (int i = 0; i < 2; i++)
                    {
                        BirdHealth health = Birds[i].GetComponent<BirdHealth>();
                        health.TakeDamage(1f, health.Species.RequiredWeaponRarity);
                    }
                    checkpoint = Time.time; stage = 2;
                }
                else if (stage == 2 && Time.time - checkpoint > 0.5f)
                {
                    Check(flock.Mode == PigeonFlockMode.Scattering, "Real damage events raise flock alarm and trigger scattering");
                    Check(flock.ActiveDivers == 0, "Panic releases attack reservations");
                    checkpoint = Time.time; stage = 3;
                }
                else if (stage == 3 && Time.time - checkpoint > 5f)
                {
                    Check(flock.Mode != PigeonFlockMode.Scattering, "Panic expires and flock regroups");
                    Check(GameManager.Instance.OpenShop(), "Shop opens in sandbox");
                    checkpoint = Time.time; stage = 4;
                }
                else if (stage == 4 && Time.time - checkpoint > 2f)
                {
                    shopPosition = Birds[0].Position; shopModeTime = flock.ModeTime;
                    checkpoint = Time.time; stage = 5;
                }
                else if (stage == 5 && Time.time - checkpoint > 1f)
                {
                    Check(Mathf.Abs(flock.ModeTime - shopModeTime) < 0.01f, "Shop freezes flock decisions");
                    Check(Vector3.Distance(Birds[0].Position, shopPosition) < 0.15f, "Shop stops pigeon motor after braking");
                    GameManager.Instance.CloseShop();
                    int count = flock.Count;
                    Birds[0].GetComponent<BirdHealth>().Kill();
                    Check(flock.Count == count - 1, "Death immediately releases flock membership");
                    for (int i = 1; i < Birds.Count; i++) UnityEngine.Object.Destroy(Birds[i].gameObject);
                    checkpoint = Time.time; stage = 6;
                }
                else if (stage == 6 && Time.time - checkpoint > 0.5f)
                {
                    Check(flock.Count == 0 && flock.ActiveDivers == 0, "Timeout-style despawn leaves no members or attack claims");
                    Check(flock.Mode == PigeonFlockMode.Roosting, "Empty flock returns to idle");
                    Birds.Clear(); Starts.Clear();
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PigeonSandboxSetup.PrefabPath);
                    for (int i = 0; i < flock.Config.maxFlockSize + 1; i++)
                    {
                        float angle = i * 2.399963f;
                        Vector3 position = player.transform.position + new Vector3(Mathf.Cos(angle) * 10f, 9f, Mathf.Sin(angle) * 10f);
                        Birds.Add(UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<PigeonBrain>());
                    }
                    checkpoint = Time.time; stage = 7;
                }
                else if (stage == 7 && Time.time - checkpoint > 1f)
                {
                    Check(flock.Count == flock.Config.maxFlockSize, "Flock respects configured capacity");
                    Check(Birds[Birds.Count - 1].State == PigeonState.Solo, "Overflow pigeon uses Solo fallback");
                    Check(GameManager.Instance.BeginWave(2), "Wave-start lifecycle hook accepts a new wave");
                    Check(flock.ActiveDivers == 0 && flock.Alarm == 0f, "New wave clears alarm and attack reservations");
                    GameManager.Instance.Pause();
                    pauseBegan = EditorApplication.timeSinceStartup;
                    shopModeTime = flock.ModeTime; shopPosition = Birds[0].Position;
                    stage = 8;
                }
                else if (stage == 8 && EditorApplication.timeSinceStartup - pauseBegan > 1f)
                {
                    Check(Mathf.Abs(flock.ModeTime - shopModeTime) < 0.01f && Vector3.Distance(Birds[0].Position, shopPosition) < 0.01f,
                        "Pause freezes decisions and physical movement");
                    GameManager.Instance.Resume();
                    checkpoint = Time.time; stage = 9;
                }
                else if (stage == 9 && Time.time - checkpoint > 12f)
                {
                    Check(Vector3.Distance(Birds[0].Position, shopPosition) > 1f, "Pigeons resume movement after pause");
                    CheckTokenLimit(flock);
                    Birds[0].GetComponent<BirdHealth>().Kill();
                    checkpoint = Time.time; stage = 10;
                }
                else if (stage == 10 && Time.time - checkpoint > 2.5f)
                {
                    Check(Birds[Birds.Count - 1].Flock == flock, "Solo bird retries and fills a vacated flock slot");
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = new Vector3(0f, 18f, 0f);
                    wall.transform.localScale = new Vector3(2f, 2f, 2f);
                    Physics.SyncTransforms();
                    Check(!PigeonNavigation.Free(wall.transform.position), "Destination safety rejects an occupied point");
                    Check(!PigeonNavigation.Clear(new Vector3(-4f, 18f, 0f), new Vector3(4f, 18f, 0f)), "Swept direct-flight query rejects an intervening wall");
                    Check(Mathf.Abs(PigeonNavigation.SegmentDistance(new Vector3(2f, 3f, 0f), Vector3.zero, new Vector3(4f, 0f, 0f)) - 3f) < 0.001f,
                        "Shot perception uses closest point on the finite shot segment");
                    UnityEngine.Object.Destroy(wall);
                    for (int i = 1; i < Birds.Count; i++) if (Birds[i] != null) UnityEngine.Object.Destroy(Birds[i].gameObject);
                    checkpoint = Time.time; stage = 11;
                }
                else if (stage == 11 && Time.time - checkpoint > 0.5f)
                {
                    Check(flock.Count == 0 && flock.ActiveDivers == 0, "Capacity fixture cleans up all flock claims");
                    Birds.Clear();
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PigeonSandboxSetup.PrefabPath);
                    Birds.Add(UnityEngine.Object.Instantiate(prefab, new Vector3(0f, 9f, 8f), Quaternion.identity).GetComponent<PigeonBrain>());
                    checkpoint = Time.time; stage = 12;
                }
                else if (stage == 12 && Time.time - checkpoint > 0.5f)
                {
                    BirdHealth health = Birds[0].GetComponent<BirdHealth>();
                    health.Kill();
                    Check(flock.Count == 0, "Last member death releases the final membership");
                    health.Revive();
                    Birds[0].enabled = true;
                    checkpoint = Time.time; stage = 13;
                }
                else if (stage == 13 && Time.time - checkpoint > 0.5f)
                {
                    Check(Birds[0].IsAlive && Birds[0].State != PigeonState.Dead && flock.Count == 1,
                        "Revived and re-enabled pigeon rejoins without stale dead state");
                    player.TakeDamage(player.MaxHP, WeaponRarity.Common);
                    checkpoint = Time.time; stage = 14;
                }
                else if (stage == 14 && Time.time - checkpoint > 0.25f)
                {
                    Check(Birds[0].State == PigeonState.FlyToRoost || Birds[0].State == PigeonState.Roosting,
                        "Player death interrupts combat with roost/rest behavior");
                    checkpoint = Time.time; stage = 15;
                }
                else if (stage == 15 && Time.time - checkpoint > 3f)
                {
                    Check(player.IsAlive && Birds[0].State != PigeonState.FlyToRoost && Birds[0].State != PigeonState.Roosting,
                        "Pigeon resumes tactics after player respawn");
                    UnityEngine.Object.Destroy(Birds[0].gameObject);
                    checkpoint = Time.time; stage = 16;
                }
                else if (stage == 16 && Time.time - checkpoint > 0.5f)
                {
                    Check(flock.Count == 0 && flock.ActiveDivers == 0, "Revive/respawn fixture releases all claims");
                    Check(errors == 0, "No runtime errors or exceptions during fixture");
                    Finish();
                }
            }
            catch (Exception exception)
            {
                Check(false, exception.ToString());
                Finish();
            }
        }

        private static void CheckTokenLimit(PigeonFlock flock)
        {
            if (flock.ActiveDivers > flock.Config.maxDivers)
                throw new InvalidOperationException("Concurrent dive limit exceeded.");
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            Results.Add("\nRuntime acceptance failures: " + failures + ". Separate Unity Editor indexing issues: " + editorIssues
                + ". Dives: " + PigeonStats.Dives + "; hits: " + PigeonStats.Hits + "; path failures: " + PigeonStats.PathFailures + ".");
            if (samples > 0) Results.Add("\nSix-bird scoped brain + flock strategy CPU: mean " + (meanNs / samples / 1000000d).ToString("F4")
                + " ms; observed peak " + (peakNs / 1000000d).ToString("F4") + " ms. Editor samples only; excludes motor/rendering/other species. Not a standalone or whole-frame certification.");
            else Results.Add("\nProfiler markers unavailable; no performance claim.");
            brainRecorder.Dispose(); flockRecorder.Dispose();
            Directory.CreateDirectory("Logs/Pigeon");
            File.WriteAllLines("Logs/Pigeon/Acceptance.md", Results);
            Debug.Log("[PigeonAcceptance] " + failures + " failures. Report: Logs/Pigeon/Acceptance.md");
            EditorApplication.isPlaying = false;
        }
    }
}
