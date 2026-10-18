using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RunwayPanic.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>One short sandbox check; all objects and simulated shot context are Play-mode only.</summary>
    [InitializeOnLoad]
    public static class GullG5SmokeCheck
    {
        private const string Key = "RunwayPanic.GullG5Smoke";
        private static readonly List<string> Report = new List<string>();
        private static GullBrain[] birds;
        private static FodItem[] props;
        private static GullBrain holder, distant;
        private static GullKettle kettle;
        private static int stage, failures;
        private static bool running;
        private static double began;
        private static float hitTime;

        static GullG5SmokeCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Tools/IS/Gull/Run Focused G5 Distress Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != GullG1Setup.SandboxPath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Open/save the Gull sandbox outside Play first.");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                stage = failures = 0; running = true; began = EditorApplication.timeSinceStartup;
                birds = null; holder = distant = null; Report.Clear();
                Report.Add("Gull G5 focused check — Unity " + Application.unityVersion);
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running) Finish("Interrupted");
            else if (change == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 50) throw new TimeoutException("Distress check timed out at stage " + stage);
                if (EditorApplication.timeSinceStartup - began < 1) return;
                if (stage == 0)
                {
                    kettle = GullKettle.Instance;
                    kettle.Configure(AssetDatabase.LoadAssetAtPath<GullStrategyConfig>(GullG1Setup.ConfigPath));
                    kettle.DispatchEnabled = false;
                    UnityEngine.Object.FindObjectsByType<BirdTestSpawner>(FindObjectsSortMode.None).Single().Spawn();
                    birds = UnityEngine.Object.FindObjectsByType<GullBrain>(FindObjectsSortMode.None);
                    distant = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GullG1Setup.PrefabPath), new Vector3(45, 12, 0), Quaternion.identity).GetComponent<GullBrain>();
                    distant.name = "G5_DistantGull";
                    // Keep this control outside the hearing radius without disabling its health/brain events.
                    ((Behaviour)distant.GetComponent<IBirdMotor>()).enabled = false;
                    stage = 1;
                }
                else if (stage == 1 && kettle.Members.Count == 5 && birds.All(b => b.State == GullState.InKettle && b.CarriedFod != null))
                {
                    holder = kettle.Members[kettle.Members.Count - 1];
                    props = birds.Select(b => b.CarriedFod).ToArray();
                    kettle.DispatchEnabled = true;
                    Check(kettle.TakeToken(holder), "Bottom Gull owns bombing token before damage");
                    kettle.DispatchEnabled = false;
                    var health = holder.GetComponent<BirdHealth>();
                    // Exercise the hub's real shot recording callback; no weapon/ammo assets are changed.
                    Vector3 origin = PlayerEventHub.Instance.PlayerPosition + Vector3.up;
                    typeof(PlayerEventHub).GetMethod("HandleShotFired", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(PlayerEventHub.Instance, new object[] { origin, holder.transform.position, health });
                    health.TakeDamage(1f, WeaponRarity.Legendary);
                    Check(health.IsAlive && health.LastHitShotOrigin == origin, "Surviving wrong-rarity hit records the simulated shooter origin");
                    Check(birds.All(b => b.State == GullState.DistressDive), "All five nearby Gulls respond immediately");
                    Check(distant.State != GullState.DistressDive, "Distant Gull ignores the call");
                    Check(kettle.TokenHolder == null && birds.All(b => !kettle.Contains(b)), "Distress returns token and removes formation membership");
                    Check(birds.Select((b, i) => b.CarriedFod == props[i] && props[i].transform.parent == b.transform).All(ok => ok), "All five retain their original carried FOD");
                    Check(birds.All(b => Mathf.Approximately(b.GetComponent<IBirdMotor>().SpeedMultiplier, b.Config.distressDiveSpeedMultiplier)), "Distress speed multiplier applied");
                    hitTime = Time.time; stage = 2;
                }
                else if (stage == 2 && Time.time - hitTime > .2f)
                {
                    float age = holder.StateTime;
                    holder.GetComponent<BirdHealth>().TakeDamage(1f, WeaponRarity.Legendary);
                    Check(holder.StateTime >= age && age > .1f, "Repeated surviving hit does not restart the dive during cooldown");
                    stage = 3;
                }
                else if (stage == 3 && Time.time - hitTime > 4.2f)
                {
                    Check(birds.All(b => b.State != GullState.DistressDive), "All five finish their one pass within four active seconds");
                    Check(birds.All(b => Mathf.Approximately(b.GetComponent<IBirdMotor>().SpeedMultiplier, 1f)), "Normal movement speed restored");
                    Check(birds.All(b => b.CarriedFod != null), "Debris is still carried while reforming");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(string error)
        {
            running = false; EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            if (error != null) Check(false, error);
            Report.Add("Simulated shot callback + actual BirdHealth damage/motors. Not an end-to-end gun, roof, lethal-hit, cooldown-expiry or performance test.");
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines("Docs/IS/Gull/G5Validation.txt", Report);
            Debug.Log("[GullG5Check] Finished with " + failures + " failures; see G5Validation.txt.");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
