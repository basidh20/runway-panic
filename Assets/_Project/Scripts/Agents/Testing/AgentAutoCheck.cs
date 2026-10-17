using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RunwayPanic.Agents.Testing
{
    /// <summary>
    /// Started by Tools > Agents > Run Automated Check: plays Sandbox_Chamika with nobody at the keyboard.
    /// 1. Flight: screenshots the overview, then chases each bird and screenshots it twice. Meanwhile it measures every
    ///    bird's largest bank, climb/dive angle and turn rate, and its largest jump between two frames, and writes them
    ///    to Docs/AgentController/flight_check.md (evidence that birds bank within limits and never snap).
    /// 2. Hits one bird and kills another, with screenshots of the flinch, the fall and the body on the ground.
    /// 3. Hides the four test birds and runs <see cref="BirdStressTest"/> once per configuration; each run appends its
    ///    FPS and frame timings to Docs/AgentController/stress_report.md.
    /// 4. Leaves Play mode.
    /// Screenshots go to Logs/AgentCheck/ (Logs is not committed). Everything happens at runtime, so the saved scene
    /// is never changed.
    /// </summary>
    public sealed class AgentAutoCheck : MonoBehaviour
    {
        /// <summary>Editor session flag set by the menu item; the next Play starts the check.</summary>
        public const string SessionKey = "RunwayPanic.Agents.AutoCheck";

        private const string ShotFolder = "Logs/AgentCheck";
        private const string FlightReportPath = "Docs/AgentController/flight_check.md";

        // Stress runs: cheapest first. The animated runs alternate 4-bone (Auto on PC) and 2-bone skinning twice, so a
        // one-off hitch (shader warm-up) in the first animated run cannot decide the comparison.
        private static readonly (BirdStressTest.Mode Mode, SkinQuality Skin)[] StressRuns =
        {
            (BirdStressTest.Mode.Static, SkinQuality.Auto),
            (BirdStressTest.Mode.MovingNoAnimation, SkinQuality.Auto),
            (BirdStressTest.Mode.MovingAndAnimated, SkinQuality.Auto),
            (BirdStressTest.Mode.MovingAndAnimated, SkinQuality.Bone2),
            (BirdStressTest.Mode.MovingAndAnimated, SkinQuality.Auto),
            (BirdStressTest.Mode.MovingAndAnimated, SkinQuality.Bone2)
        };

        private sealed class FlightStats
        {
            public BirdMotor Bird;
            public Vector3 LastPosition;
            public float LastYaw;
            public float MaxBank;
            public float MaxPitch;
            public float MaxTurnRate;
            public float WorstStepRatio;
        }

        private readonly List<FlightStats> flight = new List<FlightStats>();
        private bool sampling;
        private float samplingStart;
        private float samplingSeconds;
        private string folder;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            if (!UnityEditor.SessionState.GetBool(SessionKey, false))
                return;

            UnityEditor.SessionState.SetBool(SessionKey, false);
            new GameObject("_AgentAutoCheck").AddComponent<AgentAutoCheck>();
        }
#endif

        private IEnumerator Start()
        {
            folder = Path.Combine(Directory.GetCurrentDirectory(), ShotFolder);
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "*.png"))
                File.Delete(old);

            var rig = FindAnyObjectByType<SandboxCameraRig>();
            if (rig == null)
            {
                Debug.LogWarning("[Agents] Automated check: no SandboxCameraRig - open Sandbox_Chamika first.");
                yield break;
            }

            var birds = new List<BirdMotor>();
            foreach (Transform bird in rig.Birds)
            {
                var motor = bird != null ? bird.GetComponent<BirdMotor>() : null;
                if (motor == null)
                    continue;
                birds.Add(motor);
                flight.Add(new FlightStats { Bird = motor, LastPosition = motor.transform.position, LastYaw = motor.transform.eulerAngles.y });
            }

            if (birds.Count < rig.Birds.Count)
            {
                Debug.LogWarning("[Agents] Automated check: the sandbox birds have no BirdMotor. Run Tools > Agents > " +
                                 "Setup Bird Prefabs, then Create Agent Sandbox > Rebuild, then run the check again.");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                yield break;
            }

            // 1. Flight: overview, then each bird from behind, while the stats are sampled every frame.
            yield return new WaitForSeconds(0.5f); // let the feeders place the birds before sampling starts
            ResetLastFrame();
            sampling = true;
            samplingStart = Time.time;

            yield return new WaitForSeconds(1.5f);
            yield return Shot("00_overview");
            for (int i = 0; i < birds.Count; i++)
            {
                rig.Follow(i);
                yield return new WaitForSeconds(2.5f);
                yield return Shot($"1{i}_{birds[i].name}_a");
                yield return new WaitForSeconds(0.35f);
                yield return Shot($"1{i}_{birds[i].name}_b");
            }

            sampling = false;
            samplingSeconds = Time.time - samplingStart;
            WriteFlightReport();

            // 2. Hit (pigeon) and death (gull).
            if (birds.Count >= 2)
            {
                rig.Follow(1);
                yield return new WaitForSeconds(1.5f);
                BirdHealth pigeon = birds[1].GetComponent<BirdHealth>();
                pigeon.TakeDamage(1f, pigeon.Species != null ? pigeon.Species.RequiredWeaponRarity : default);
                yield return new WaitForSeconds(0.08f);
                yield return Shot("20_hit_flinch");

                rig.Follow(0);
                yield return new WaitForSeconds(1.5f);
                birds[0].GetComponent<BirdHealth>().Kill();
                yield return new WaitForSeconds(0.3f);
                yield return Shot("21_death_0.3s");
                yield return new WaitForSeconds(0.7f);
                yield return Shot("22_death_1.0s");
                yield return new WaitForSeconds(0.8f);
                yield return Shot("23_death_1.8s");
            }

            // 3. Stress test, one run per configuration, with the test birds out of the way.
            foreach (TestPathFeeder feeder in FindObjectsByType<TestPathFeeder>())
                feeder.gameObject.SetActive(false);
            foreach (BirdMotor bird in birds)
            {
                if (bird != null)
                    bird.gameObject.SetActive(false);
            }
            rig.ShowOverview();

            BirdMotor[] prefabs = FindStressPrefabs();
            if (prefabs == null)
            {
                Debug.LogWarning("[Agents] Automated check: no StressTest object with prefabs in the scene.");
            }
            else
            {
                for (int r = 0; r < StressRuns.Length; r++)
                {
                    var runObject = new GameObject($"_Stress_{StressRuns[r].Mode}_{StressRuns[r].Skin}");
                    var test = runObject.AddComponent<BirdStressTest>();
                    test.Configure(prefabs, StressRuns[r].Mode, StressRuns[r].Skin);

                    yield return new WaitForSeconds(1f); // during the warm-up, so the screenshot is not measured
                    yield return Shot($"3{r}_stress_{StressRuns[r].Mode}_{StressRuns[r].Skin}");
                    while (!test.IsFinished)
                        yield return null;

                    Destroy(runObject);
                    yield return null;
                }
            }

            Debug.Log($"[Agents] Automated check finished. Screenshots: {ShotFolder}/, results: {FlightReportPath}, Docs/AgentController/stress_report.md");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void Update()
        {
            if (!sampling || Time.deltaTime <= 0f)
                return;

            foreach (FlightStats stats in flight)
            {
                BirdMotor bird = stats.Bird;
                if (bird == null || !bird.gameObject.activeInHierarchy)
                    continue;

                BirdMotor follower = bird;
                Transform t = bird.transform;
                float yaw = t.eulerAngles.y;

                stats.MaxBank = Mathf.Max(stats.MaxBank, Mathf.Abs(follower.BankAngle));
                stats.MaxPitch = Mathf.Max(stats.MaxPitch, Mathf.Abs(follower.PitchAngle));
                stats.MaxTurnRate = Mathf.Max(stats.MaxTurnRate, Mathf.Abs(Mathf.DeltaAngle(stats.LastYaw, yaw)) / Time.deltaTime);

                // How far the bird moved this frame compared with the furthest it could move at top speed:
                // above 1 would mean a jump (snap), not flight.
                float allowed = Mathf.Max(follower.BaseSpeed * Time.deltaTime, 1e-4f);
                stats.WorstStepRatio = Mathf.Max(stats.WorstStepRatio, (t.position - stats.LastPosition).magnitude / allowed);

                stats.LastPosition = t.position;
                stats.LastYaw = yaw;
            }
        }

        private void ResetLastFrame()
        {
            foreach (FlightStats stats in flight)
            {
                stats.LastPosition = stats.Bird.transform.position;
                stats.LastYaw = stats.Bird.transform.eulerAngles.y;
            }
        }

        private void WriteFlightReport()
        {
            var text = new StringBuilder();
            text.AppendLine("# Flight check");
            text.AppendLine();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Written by **Tools > Agents > Run Automated Check** on {0:yyyy-MM-dd HH:mm}: {1:0.0} s of flight in `Sandbox_Chamika`, " +
                "with `TestPathFeeder` re-planning every bird's path every 5 s. Every run overwrites this file.", DateTime.Now, samplingSeconds));
            text.AppendLine();
            text.AppendLine("| Bird | Top speed (m/s) | Max bank (°) | Bank limit (°) | Max climb/dive (°) | Pitch limit (°) | Max turn rate (°/s) | Turn limit (°/s) | Largest frame move ÷ (top speed × frame time) |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (FlightStats stats in flight)
            {
                BirdMotionProfile motion = stats.Bird.Motion;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| `{0}` | {1:0.0} | {2:0.0} | {3:0} | {4:0.0} | {5:0} | {6:0} | {7:0} | {8:0.00} |",
                    stats.Bird.name, stats.Bird.BaseSpeed, stats.MaxBank, motion.MaxBank, stats.MaxPitch, motion.MaxPitch,
                    stats.MaxTurnRate, motion.MaxTurnRate, stats.WorstStepRatio));
            }
            text.AppendLine();
            text.AppendLine("The last column stays at or below 1.00 when a bird never moves further in one frame than its top speed allows,");
            text.AppendLine("i.e. no jump or snap, including at the moments its path is replaced. Turn rate is measured on the drawn");
            text.AppendLine("(interpolated) rotation, so it can slightly exceed the limit when a frame is short.");

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), FlightReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text.ToString());
        }

        /// <summary>The prefabs of the (disabled) StressTest object in the open scene.</summary>
        private static BirdMotor[] FindStressPrefabs()
        {
            foreach (BirdStressTest test in Resources.FindObjectsOfTypeAll<BirdStressTest>())
            {
                if (test.gameObject.scene.IsValid() && test.Prefabs != null && test.Prefabs.Length > 0)
                    return test.Prefabs;
            }
            return null;
        }

        /// <summary>
        /// Screenshot of the Game view. It is taken at the end of the frame, so the caller waits one frame before
        /// changing anything (e.g. the camera target).
        /// </summary>
        private IEnumerator Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
            yield return null;
        }
    }
}
