using System;
using System.Collections.Generic;
using System.IO;
using RunwayPanic.AI;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>One opt-in sandbox check of real carry, collisions, damage and navigation cleanup.</summary>
    [InitializeOnLoad]
    public static class GullFodSmokeCheck
    {
        private const string SessionKey = "RunwayPanic.GullFodSmoke";
        private static readonly List<string> Report = new List<string>();
        private static bool running;
        private static int stage, failures, impacts;
        private static double began;
        private static float hpBefore, expectedDamage;
        private static bool hitPlayer, timedItemLanded;
        private static FodItem first, second, timed;
        private static GullStrategyConfig lifetimeConfig;
        private static PlayerHealth player;
        private static FlightGrid grid;
        private static GameObject carrier, prefab;
        private static Vector3Int cell, timedCell;

        static GullFodSmokeCheck() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Tools/IS/Gull/Run Focused FOD Item Check")]
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
                stage = failures = impacts = 0; hitPlayer = timedItemLanded = false;
                first = second = timed = null; carrier = null; lifetimeConfig = null;
                Report.Clear(); running = true; began = EditorApplication.timeSinceStartup;
                Report.Add("Gull G2a — focused runtime FOD item check (Unity " + Application.unityVersion + ")");
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
                if (EditorApplication.timeSinceStartup - began > 25) throw new TimeoutException("FOD check exceeded 25 seconds.");
                if (EditorApplication.timeSinceStartup - began < 1) return;
                if (stage == 0)
                {
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
                    grid = FlightGrid.Instance;
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullFodSetup.PrefabPath);
                    if (player == null || grid == null || !grid.IsBuilt || prefab == null)
                        throw new InvalidOperationException("Missing player, built grid or FOD prefab.");
                    hpBefore = player.CurrentHP;
                    carrier = new GameObject("FOD_CheckCarrier");
                    carrier.transform.SetPositionAndRotation(player.transform.position + Vector3.up * 4, Quaternion.identity);
                    carrier.transform.localScale = Vector3.one * .6f;
                    first = UnityEngine.Object.Instantiate(prefab).GetComponent<FodItem>();
                    first.AttachTo(carrier.transform, new Vector3(0, -.15f, .05f));
                    var body = first.GetComponent<Rigidbody>();
                    var box = first.GetComponent<BoxCollider>();
                    Check(first.State == FodItem.FodState.Carried && body.isKinematic && !box.enabled && first.transform.parent == carrier.transform,
                        "Carry parents to a scaled carrier, disables collision and keeps the body kinematic");
                    Check(Vector3.Distance(Vector3.Scale(first.transform.lossyScale, box.size), new Vector3(.3f, .15f, .4f)) < .001f,
                        "Carry preserves the prop's 0.3 x 0.15 x 0.4 m world dimensions on a scale-0.6 carrier");
                    Vector3 before = first.transform.position;
                    carrier.transform.position += Vector3.up * .5f;
                    Check(Vector3.Distance(first.transform.position, before + Vector3.up * .5f) < .001f, "Carried prop follows carrier movement");
                    expectedDamage = first.Damage;
                    first.Impacted += (item, hit) => { impacts++; hitPlayer |= hit; };
                    first.Release();
                    Check(first.State == FodItem.FodState.Falling && first.transform.parent == null && !body.isKinematic &&
                        body.useGravity && box.enabled && body.linearVelocity == Vector3.zero,
                        "Release detaches with gravity, collision and zero inherited velocity");
                    cell = grid.ClampToGrid(first.transform.position - Vector3.up * 4);
                    cell.y = 0;
                    Check(!grid.IsBlocked(cell), "Selected bottom grid cell starts clear before debris lands");
                    stage = 1;
                }
                else if (stage == 1 && first != null && first.State == FodItem.FodState.Landed)
                {
                    var box = first.GetComponent<BoxCollider>();
                    cell = grid.ClampToGrid(box.bounds.center);
                    Check(impacts == 1 && hitPlayer && Mathf.Abs(hpBefore - player.CurrentHP - expectedDamage) < .01f,
                        "Real drop hits the player once for configured Gull damage (" + expectedDamage + ") across all collision contacts");
                    var obstacle = first.GetComponent<NavMeshObstacle>();
                    Check(first.transform.position.y < .5f && first.GetComponent<Rigidbody>().isKinematic && obstacle != null &&
                        obstacle.enabled && obstacle.carving && grid.IsBlocked(cell),
                        "After the player hit, debris reaches airport ground and enables carving/manual grid blocking");
                    first.gameObject.SetActive(false);
                    Check(!grid.IsBlocked(cell), "Disabling debris releases its manual block and stale geometry immediately");
                    first.gameObject.SetActive(true);
                    Check(grid.IsBlocked(cell), "Re-enabling landed debris restores its block");
                    second = UnityEngine.Object.Instantiate(prefab, first.transform.position + Vector3.up * .5f, Quaternion.identity).GetComponent<FodItem>();
                    second.Release();
                    stage = 2;
                }
                else if (stage == 2 && second != null && second.State == FodItem.FodState.Landed)
                {
                    Check(grid.ClampToGrid(second.GetComponent<BoxCollider>().bounds.center) == cell, "Two physical debris pieces occupy the same bottom grid cell");
                    first.Despawn();
                    Check(grid.IsBlocked(cell), "Removing one overlapping piece preserves the other piece's block");
                    second.Despawn();
                    Check(!grid.IsBlocked(cell), "Removing the last overlapping piece restores the clear cell");
                    lifetimeConfig = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GullStrategyConfig>(GullG1Setup.ConfigPath));
                    lifetimeConfig.fodLifetime = .25f; // Transient test copy; the shared asset stays at 40 seconds.
                    Report.Add("INFO: Previous lifetime-test location (6,0,-24) already blocked by airport geometry: " +
                        grid.IsBlocked(grid.ClampToGrid(new Vector3(6, .8f, -24))));
                    Check(!grid.IsBlocked(grid.ClampToGrid(new Vector3(2, .8f, -5))), "Lifetime-test runway cell is clear before the timed drop");
                    timed = UnityEngine.Object.Instantiate(prefab, new Vector3(2, 3, -5), Quaternion.identity).GetComponent<FodItem>();
                    timed.Configure(lifetimeConfig, AssetDatabase.LoadAssetAtPath<BirdSpeciesData>("Assets/_Project/ScriptableObjects/Birds/BirdSpecies_Gull.asset"));
                    timed.Settled += item => { timedItemLanded = true; timedCell = grid.ClampToGrid(item.GetComponent<BoxCollider>().bounds.center); };
                    timed.Release();
                    stage = 3;
                }
                else if (stage == 3 && timed == null)
                {
                    Check(timedItemLanded && !grid.IsBlocked(timedCell), "Configured landed lifetime despawns debris and releases navigation; tested with a 0.25 s transient config (landed=" +
                        timedItemLanded + ", cell=" + timedCell + ", blocked=" + grid.IsBlocked(timedCell) + ")");
                    Check(impacts == 1, "Additional contacts never raised a second impact for the first release");
                    Finish(true);
                }
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
            if (first != null) first.Despawn();
            if (second != null) second.Despawn();
            if (timed != null) timed.Despawn();
            if (carrier != null) UnityEngine.Object.Destroy(carrier);
            if (lifetimeConfig != null) UnityEngine.Object.Destroy(lifetimeConfig);
            Report.Add("One item-lifecycle check only. No supply manager, cooldown/claims, automatic gathering, full AI suite or baked NavMesh carving test.");
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull");
            File.WriteAllLines("Docs/IS/Gull/G2aValidation.txt", Report);
            Debug.Log("[GullFODCheck] Finished with " + failures + " failures; see Docs/IS/Gull/G2aValidation.txt.");
            if (stopPlay) EditorApplication.isPlaying = false;
        }
    }
}
