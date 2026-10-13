using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Tools > S3 > Performance Test: evidence for the "no triangle limits" decision (DECISIONS.md, 11 Oct).
    ///   Create Test Wave  - the player + an equal mix of the 4 bird species, spread inside the main camera's view.
    ///                       Root "_ArtPerfTest" is tagged EditorOnly, so it never ends up in a build.
    ///   Measure           - starts AUTOMATICALLY when Play mode starts in a scene that contains "_ArtPerfTest"
    ///                       (active root = wave row, unticked root = baseline row), or via the menu in Play mode.
    ///                       2 s warm-up, then 10 s of samples (FPS, CPU / render ms, tris, draw calls,
    ///                       SetPass calls, SRP Batcher draw calls...), appended to SourceArt/perf_report.md.
    /// Editor-only: no runtime scripts, nothing in the build, no teammate code touched.
    /// </summary>
    [InitializeOnLoad]
    public static class ArtPerfTest
    {
        const string RootName = "_ArtPerfTest";
        const string ReportPath = "SourceArt/perf_report.md";
        const string PrefabFolder = "Assets/_Project/Prefabs/";
        const string PlayerPrefab = PrefabFolder + "Player/P_Player_Officer.prefab";
        static readonly string[] Species = { "Gull", "Pigeon", "Crow", "Hawk" };

        const float MinDepth = 8f, MaxDepth = 35f; // metres in front of the camera
        const float ViewFill = 0.8f;               // use 80% of the view so birds are not cut at the edges
        const float MinBirdHeight = 2f;            // keep birds above the ground plane
        const float PlayerDistance = 6f;
        const int Seed = 12345;                    // same layout every run, so results are comparable

        const double WarmUpSeconds = 2.0, MeasureSeconds = 10.0, WindowSeconds = 0.5;

        static ArtPerfTest()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && FindRoot() != null && !measuring) StartMeasure();
        }

        // GameObject.Find skips inactive objects; the root is searched by name so an unticked root is found too.
        static GameObject FindRoot() =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == RootName);

        // ------------------------------------------------------------------ wave

        [MenuItem("Tools/S3/Performance Test/Create Test Wave (40 birds)")]
        static void CreateWave40() => CreateWave(10);

        [MenuItem("Tools/S3/Performance Test/Create Test Wave (100 birds)")]
        static void CreateWave100() => CreateWave(25);

        [MenuItem("Tools/S3/Performance Test/Remove Test Wave")]
        static void RemoveWave()
        {
            var existing = FindRoot();
            if (existing != null) Undo.DestroyObjectImmediate(existing);
        }

        static void CreateWave(int perSpecies)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[Art] Performance test: no camera tagged MainCamera in the open scene.");
                return;
            }

            RemoveWave();
            var root = new GameObject(RootName) { tag = "EditorOnly" };
            Undo.RegisterCreatedObjectUndo(root, "Create Performance Test Wave");

            // Player on the ground in front of the camera, facing away (third-person view).
            Vector3 flatForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (flatForward == Vector3.zero) flatForward = Vector3.forward;
            Vector3 playerPos = camera.transform.position + flatForward * PlayerDistance;
            playerPos.y = 0f;
            Spawn(PlayerPrefab, root.transform, playerPos, Quaternion.LookRotation(flatForward));

            var random = new Random(Seed);
            float tanHalfFov = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            int spawned = 0;
            for (int i = 0; i < perSpecies; i++)
            {
                foreach (string species in Species)
                {
                    float depth = Mathf.Lerp(MinDepth, MaxDepth, (float)random.NextDouble());
                    float halfHeight = depth * tanHalfFov * ViewFill;
                    float halfWidth = halfHeight * camera.aspect;
                    Vector3 pos = camera.transform.position
                                  + camera.transform.forward * depth
                                  + camera.transform.right * Mathf.Lerp(-halfWidth, halfWidth, (float)random.NextDouble())
                                  + camera.transform.up * Mathf.Lerp(-halfHeight, halfHeight, (float)random.NextDouble());
                    pos.y = Mathf.Max(pos.y, MinBirdHeight);
                    var yaw = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                    if (Spawn($"{PrefabFolder}Birds/P_Bird_{species}.prefab", root.transform, pos, yaw)) spawned++;
                }
            }

            Selection.activeGameObject = root;
            Debug.Log($"[Art] Performance test wave: player + {spawned} birds ({perSpecies} per species). " +
                      "Press Play: the measurement starts by itself.");
        }

        static bool Spawn(string prefabPath, Transform parent, Vector3 position, Quaternion rotation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[Art] Performance test: {prefabPath} not found.");
                return false;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.SetPositionAndRotation(position, rotation);
            return true;
        }

        // ------------------------------------------------------------------ measurement

        // UnityStats is the data behind the Game view's Stats panel. It is undocumented and its members change
        // between versions, so every value is read by name: a missing one shows as "n/a" instead of breaking compilation.
        static readonly Type StatsType = Type.GetType("UnityEditor.UnityStats, UnityEditor.CoreModule");

        static readonly (string Property, string Label)[] StatNames =
        {
            ("triangles", "Triangles"),
            ("vertices", "Vertices"),
            ("drawCalls", "Draw calls (Batches)"),
            ("setPassCalls", "SetPass calls"),
            ("srpBatcherDrawCalls", "SRP Batcher draw calls"),
            ("shadowCasters", "Shadow casters"),
            ("visibleSkinnedMeshes", "Visible skinned meshes"),
            // The Stats panel's "CPU: main" value. It is the main thread's time for the whole frame, the wait for the
            // render thread and the GPU included, so on its own it cannot tell a CPU limit from a GPU limit.
            ("frameTime", "Stats panel main thread, waits included (ms)"),
            ("renderTime", "Render thread (ms)"),
        };

        // FrameTimingManager splits the frame: main thread work, its wait for the present, render thread, GPU.
        // Main thread minus present wait = real CPU work; a GPU time close to the frame time = GPU limited.
        static readonly string[] TimingLabels =
        {
            "Frame timing: CPU frame (ms)",
            "Frame timing: main thread (ms)",
            "Frame timing: main thread waiting for present (ms)",
            "Frame timing: render thread (ms)",
            "Frame timing: GPU frame (ms)",
        };

        static readonly FrameTiming[] latestTiming = new FrameTiming[1];
        static double[] timingSums;
        static int timingSamples, gpuSamples;

        static bool measuring;
        static double startTime, windowStart;
        static int startFrame, windowFrame;
        static double[] statSums;
        static int statSamples;
        static double minWindowFps;

        [MenuItem("Tools/S3/Performance Test/Measure (Play Mode)")]
        static void StartMeasure()
        {
            measuring = true;
            startTime = EditorApplication.timeSinceStartup;
            startFrame = -1;
            statSums = new double[StatNames.Length];
            statSamples = 0;
            timingSums = new double[TimingLabels.Length];
            timingSamples = gpuSamples = 0;
            minWindowFps = double.MaxValue;
            EditorApplication.update += Tick;
            Debug.Log($"[Art] Performance test: measuring for {WarmUpSeconds + MeasureSeconds:0} s - keep the Game view visible.");
        }

        [MenuItem("Tools/S3/Performance Test/Measure (Play Mode)", true)]
        static bool CanMeasure() => EditorApplication.isPlaying && !measuring;

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                Stop();
                Debug.LogWarning("[Art] Performance test stopped: left Play mode.");
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - startTime < WarmUpSeconds) return; // shader compilation / first-frame spikes

            if (startFrame < 0)
            {
                startFrame = windowFrame = Time.frameCount;
                startTime = windowStart = now;
                return;
            }

            for (int i = 0; i < StatNames.Length; i++) statSums[i] += ReadStat(StatNames[i].Property) ?? double.NaN;
            statSamples++;
            SampleFrameTiming();

            if (now - windowStart >= WindowSeconds)
            {
                minWindowFps = Math.Min(minWindowFps, (Time.frameCount - windowFrame) / (now - windowStart));
                windowFrame = Time.frameCount;
                windowStart = now;
            }

            if (now - startTime < MeasureSeconds) return;

            double avgFps = (Time.frameCount - startFrame) / (now - startTime);
            Stop();
            WriteReport(avgFps);
        }

        static void SampleFrameTiming()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, latestTiming) == 0) return;

            FrameTiming timing = latestTiming[0];
            timingSums[0] += timing.cpuFrameTime;
            timingSums[1] += timing.cpuMainThreadFrameTime;
            timingSums[2] += timing.cpuMainThreadPresentWaitTime;
            timingSums[3] += timing.cpuRenderThreadFrameTime;
            timingSamples++;
            // The GPU time arrives a few frames late and is 0 where the graphics API does not report it.
            if (timing.gpuFrameTime > 0)
            {
                timingSums[4] += timing.gpuFrameTime;
                gpuSamples++;
            }
        }

        static void Stop()
        {
            measuring = false;
            EditorApplication.update -= Tick;
        }

        static double? ReadStat(string property)
        {
            var info = StatsType?.GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (info == null) return null;
            try { return Convert.ToDouble(info.GetValue(null)); }
            catch (Exception) { return null; }
        }

        static void WriteReport(double avgFps)
        {
            // An unticked wave root counts as "no wave" (baseline run).
            var root = FindRoot();
            var children = root != null && root.activeSelf
                ? root.transform.Cast<Transform>().Where(t => t.gameObject.activeSelf).ToList()
                : new System.Collections.Generic.List<Transform>();
            int birds = children.Count(t => t.name.StartsWith("P_Bird_"));
            bool player = children.Any(t => t.name.StartsWith("P_Player"));
            int skinned = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            Vector2 gameView = Handles.GetMainGameViewSize();

            var sb = new StringBuilder();
            if (!File.Exists(ReportPath))
            {
                sb.AppendLine("# Performance report");
                sb.AppendLine();
                sb.AppendLine("Generated by Tools > S3 > Performance Test (`ArtPerfTest.cs`). Each run: 2 s warm-up, then 10 s of samples.");
                sb.AppendLine("Measured in **Editor Play mode**: editor overhead is included, so a standalone build is usually faster.");
                sb.AppendLine("No animation clips yet: birds are static, but every SkinnedMeshRenderer is still skinned each frame.");
                sb.AppendLine();
            }

            string wave = birds == 0 && !player ? "no test wave (baseline)"
                : player ? $"player + {birds} birds" : $"{birds} birds, no player";
            sb.AppendLine($"## {DateTime.Now:yyyy-MM-dd HH:mm} — {wave}");
            sb.AppendLine();
            sb.AppendLine($"- Scene: `{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}`, Game view {gameView.x:0}×{gameView.y:0}, " +
                          $"VSync count {QualitySettings.vSyncCount}, target frame rate {Application.targetFrameRate}");
            sb.AppendLine($"- Machine: {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                          $"{SystemInfo.systemMemorySize / 1024f:0.#} GB RAM, Unity {Application.unityVersion}");
            sb.AppendLine($"- Active SkinnedMeshRenderers in the scene: {skinned}");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine($"| Average FPS | {avgFps:0.0} |");
            sb.AppendLine($"| Lowest FPS (0.5 s windows) | {(minWindowFps == double.MaxValue ? avgFps : minWindowFps):0.0} |");
            sb.AppendLine($"| Average frame time (ms) | {1000.0 / avgFps:0.00} |");
            for (int i = 0; i < StatNames.Length; i++)
            {
                double avg = statSamples > 0 ? statSums[i] / statSamples : double.NaN;
                // UnityStats reports frameTime / renderTime in seconds: shown in ms.
                string value = double.IsNaN(avg) ? "n/a" : StatNames[i].Property.EndsWith("Time") ? (avg * 1000.0).ToString("0.00") : avg.ToString("N0");
                sb.AppendLine($"| {StatNames[i].Label} (avg) | {value} |");
            }
            for (int i = 0; i < TimingLabels.Length; i++)
            {
                int samples = i == 4 ? gpuSamples : timingSamples;
                sb.AppendLine($"| {TimingLabels[i]} (avg) | {(samples > 0 ? (timingSums[i] / samples).ToString("0.00") : "n/a")} |");
            }
            sb.AppendLine();

            File.AppendAllText(ReportPath, sb.ToString());
            Debug.Log($"[Art] Performance test: {avgFps:0.0} FPS average ({wave}) - written to {ReportPath}.");
        }
    }
}
