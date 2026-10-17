using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RunwayPanic.Agents.Testing
{
    /// <summary>
    /// Performance test for the Agent Controller: spawns many birds, each flying its own looping grid path, then
    /// measures the frame rate for a fixed time (after a warm-up) and logs it. Run it once per Mode on the same
    /// machine to see what movement and animation cost on top of static birds. Results go to the Console and, in the
    /// Editor, are appended to Docs/AgentController/stress_report.md.
    /// The same seed gives the same birds and paths every run, so runs are comparable.
    /// </summary>
    public sealed class BirdStressTest : MonoBehaviour
    {
        public enum Mode
        {
            MovingAndAnimated = 0,  // the real game
            MovingNoAnimation = 1,  // follower on, Animator off: the cost of movement alone
            Static = 2              // everything off: the cost of drawing the birds
        }

        private const string ReportPath = "Docs/AgentController/stress_report.md";
        private const float WindowSeconds = 0.5f;

        [Tooltip("Bird prefabs, used in turn (gull, pigeon, crow, hawk).")]
        [SerializeField] private BirdAgent[] prefabs = Array.Empty<BirdAgent>();
        [SerializeField, Min(1)] private int birdCount = 40;
        [SerializeField] private Mode mode = Mode.MovingAndAnimated;
        [Tooltip("Animator culling mode for the spawned birds, to compare CullUpdateTransforms with AlwaysAnimate.")]
        [SerializeField] private AnimatorCullingMode cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        [Tooltip("Birds fly loops inside this radius around this object, in metres.")]
        [SerializeField, Min(2f)] private float areaRadius = 15f;
        [SerializeField, Min(1f)] private float minAltitude = 3f;
        [SerializeField, Min(1f)] private float maxAltitude = 10f;
        [Tooltip("Grid cell size of the generated paths, in metres.")]
        [SerializeField, Min(0.25f)] private float cellSize = 2f;
        [SerializeField, Min(0f)] private float warmUpSeconds = 2f;
        [SerializeField, Min(1f)] private float measureSeconds = 10f;
        [SerializeField] private int seed = 12345;

        private float startTime;
        private float measureStart;
        private int frames;
        private float windowStart;
        private int windowFrames;
        private float lowestFps = float.MaxValue;
        private bool done;
        private string result = "Warming up...";

        private void Start()
        {
            if (prefabs.Length == 0)
            {
                Debug.LogWarning("[Agents] BirdStressTest: no prefabs assigned.", this);
                enabled = false;
                return;
            }

            var random = new System.Random(seed);
            for (int i = 0; i < birdCount; i++)
                Spawn(prefabs[i % prefabs.Length], i, random);

            startTime = Time.realtimeSinceStartup;
        }

        private void Spawn(BirdAgent prefab, int index, System.Random random)
        {
            Vector3[] loop = BuildLoop(random);
            BirdAgent bird = Instantiate(prefab, loop[0], Quaternion.identity, transform);
            bird.name = $"{prefab.name}_{index:00}";

            BirdPathFollower follower = bird.GetComponent<BirdPathFollower>();
            Animator animator = bird.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = cullingMode;

            if (mode != Mode.MovingAndAnimated)
            {
                bird.GetComponent<BirdAnimatorDriver>().enabled = false;
                if (animator != null)
                    animator.enabled = false;
            }

            if (mode == Mode.Static)
            {
                follower.enabled = false;
                return;
            }

            follower.Teleport(loop[0], loop[1] - loop[0]);
            follower.SetPath(loop);
            follower.PathCompleted += () => follower.SetPath(loop); // fly the same loop forever
        }

        /// <summary>A rectangle of random size and height, sent as one waypoint per grid cell (A* style).</summary>
        private Vector3[] BuildLoop(System.Random random)
        {
            float height = Mathf.Lerp(minAltitude, maxAltitude, (float)random.NextDouble());
            float halfX = Mathf.Lerp(4f, areaRadius, (float)random.NextDouble());
            float halfZ = Mathf.Lerp(4f, areaRadius, (float)random.NextDouble());
            Vector3 centre = transform.position + new Vector3(
                Mathf.Lerp(-areaRadius, areaRadius, (float)random.NextDouble()) * 0.3f, height,
                Mathf.Lerp(-areaRadius, areaRadius, (float)random.NextDouble()) * 0.3f);

            Vector3[] corners =
            {
                centre + new Vector3(-halfX, 0f, -halfZ), centre + new Vector3(halfX, 0f, -halfZ),
                centre + new Vector3(halfX, 0f, halfZ), centre + new Vector3(-halfX, 0f, halfZ)
            };
            if (random.Next(2) == 0)
                Array.Reverse(corners); // half the birds fly the other way round

            var loop = new List<Vector3>();
            for (int side = 0; side < corners.Length; side++)
            {
                Vector3 from = corners[side];
                Vector3 to = corners[(side + 1) % corners.Length];
                int steps = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / cellSize));
                for (int s = 0; s < steps; s++)
                    loop.Add(Vector3.Lerp(from, to, (float)s / steps));
            }
            loop.Add(corners[0]);
            return loop.ToArray();
        }

        private void Update()
        {
            if (done)
                return;

            float now = Time.realtimeSinceStartup;
            if (now - startTime < warmUpSeconds)
                return;

            if (measureStart <= 0f)
            {
                measureStart = now;
                windowStart = now;
                result = "Measuring...";
            }

            frames++;
            windowFrames++;
            if (now - windowStart >= WindowSeconds)
            {
                lowestFps = Mathf.Min(lowestFps, windowFrames / (now - windowStart));
                windowStart = now;
                windowFrames = 0;
            }

            if (now - measureStart >= measureSeconds)
                Finish(now);
        }

        private void Finish(float now)
        {
            done = true;
            float elapsed = now - measureStart;
            float averageFps = frames / elapsed;
            result = string.Format(CultureInfo.InvariantCulture,
                "{0} birds, {1}, culling {2}: {3:0.0} FPS average ({4:0.00} ms), lowest {5:0.0} FPS over {6:0.0} s windows",
                birdCount, mode, cullingMode, averageFps, 1000f / averageFps, lowestFps, WindowSeconds);
            Debug.Log("[Agents] Stress test: " + result, this);

#if UNITY_EDITOR
            string line = string.Format(CultureInfo.InvariantCulture,
                "| {0:yyyy-MM-dd HH:mm} | {1} | {2} | {3} | {4:0.0} | {5:0.00} | {6:0.0} | {7}x{8} |",
                DateTime.Now, birdCount, mode, cullingMode, averageFps, 1000f / averageFps, lowestFps, Screen.width, Screen.height);
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            if (!File.Exists(fullPath))
            {
                File.WriteAllText(fullPath,
                    "# Bird stress test results\n\n" +
                    "Appended by `BirdStressTest` (Sandbox_Chamika, object `StressTest`), Editor Play mode.\n\n" +
                    "| Date | Birds | Mode | Animator culling | Avg FPS | Avg ms | Lowest FPS (0.5 s) | Game view |\n" +
                    "|---|---:|---|---|---:|---:|---:|---|\n");
            }
            File.AppendAllText(fullPath, line + "\n");
#endif
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10f, 10f, 900f, 24f), "Bird stress test - " + result);
        }
    }
}
