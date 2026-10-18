#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using RunwayPanic.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Tools > IS > Crow > Run Wave 3 Playtest: plays the real wave 3 (crows and pigeons, from the WaveManager) in
    /// <c>Main</c> and writes <c>Docs/IS/Crow/wave3_playtest.md</c>.
    /// <list type="number">
    /// <item><b>Watch</b> (40 s, nobody shoots): which states each crow goes through, thefts, attacks on the player,
    /// and whether any crow gets stuck (barely moving for 6 s while it should be flying) or ends up inside geometry.</item>
    /// <item><b>Fight</b>: a sniper player with the real gun timing (fire rate, magazine, reload), 0.6 s to aim at each
    /// bird, and every third shot a near miss (so dodging is exercised); pigeons get the assault rifle. Records the time
    /// to clear, kills, funerals, rage and dodges.</item>
    /// </list>
    /// The player is given the sniper and the assault rifle and is kept alive; damage taken is reported. Main is never
    /// saved.
    /// </summary>
    public sealed class CrowWavePlaytest : MonoBehaviour
    {
        private const string MainScene = "Assets/_Project/Scenes/Main.unity";
        private const string ReportPath = "Docs/IS/Crow/wave3_playtest.md";
        private const string WeaponFolder = "Assets/_Project/ScriptableObjects/Weapons";
        private const string RunningKey = "RunwayPanic.CrowWavePlaytest.Running";
        private const float WatchSeconds = 40f;
        private const float FightLimit = 120f;
        private const float AimTime = 0.6f;
        private const float StuckSeconds = 6f;
        private const float StuckDistance = 0.5f;

        private readonly StringBuilder report = new StringBuilder();
        private readonly Dictionary<CrowBrain, HashSet<CrowState>> seen = new Dictionary<CrowBrain, HashSet<CrowState>>();
        private readonly Dictionary<CrowBrain, (Vector3 position, float time)> lastMove =
            new Dictionary<CrowBrain, (Vector3, float)>();
        private readonly HashSet<CrowBrain> stuck = new HashSet<CrowBrain>();
        private readonly List<string> stuckNotes = new List<string>();
        private readonly HashSet<CrowBrain> insideGeometry = new HashSet<CrowBrain>();
        private readonly List<string> insideNotes = new List<string>();
        private PlayerHealth health;
        private float damageTaken;
        private int heals;

        [MenuItem("Tools/IS/Crow/Run Wave 3 Playtest")]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Begin();
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunningKey, false))
                {
                    SessionState.SetBool(RunningKey, false);
                    Begin();
                }
            };
        }

        private static void Begin()
        {
            if (FindAnyObjectByType<CrowWavePlaytest>() == null)
                new GameObject("[CrowWavePlaytest]").AddComponent<CrowWavePlaytest>();
        }

        private IEnumerator Start()
        {
            yield return new WaitUntil(() => GameManager.Instance != null && WaveManager.Instance != null &&
                                             FindAnyObjectByType<PlayerHealth>() != null);
            yield return new WaitForSeconds(0.5f);

            GameManager game = GameManager.Instance;
            WaveManager waves = WaveManager.Instance;
            health = FindAnyObjectByType<PlayerHealth>();
            health.Damaged += amount => damageTaken += amount;
            var inventory = health.GetComponent<PlayerInventory>();
            List<WeaponData> guns = LoadGuns();
            WeaponData sniper = guns.First(g => g.Rarity == WeaponRarity.Rare);
            WeaponData rifle = guns.First(g => g.Rarity == WeaponRarity.Uncommon);
            inventory.AddWeapon(rifle.Prefab, equip: false);
            inventory.AddWeapon(sniper.Prefab, equip: true);

            // Straight to wave 3, as if waves 1 and 2 were won.
            yield return new WaitUntil(() => game.CurrentState == GameState.Playing && !game.IsWaveActive);
            game.BeginWave(3);
            WaveData data = waves.CurrentData;
            CrowStats.Reset();

            report.AppendLine("# Crows in wave 3 of the real game");
            report.AppendLine($"Scene `Main`, {System.DateTime.Now:yyyy-MM-dd HH:mm}. Generated by `Tools > IS > Crow > Run Wave 3 Playtest`. " +
                              $"Wave 3 \"{data.Title}\": " +
                              string.Join(" + ", data.SpawnGroups.Select(g => $"{g.count} {g.species.SpeciesName}")) +
                              $", {data.Duration:0} s, at most {data.MaxAlive} alive; spawned by the WaveManager at the " +
                              "level's spawn markers, with the real FlightGrid, crow setup and pickups.");
            report.AppendLine();

            // ---------- Watch ----------
            float watchEnd = Time.time + WatchSeconds;
            while (Time.time < watchEnd && waves.Phase == WaveManager.WavePhase.Running)
                yield return null;

            report.AppendLine($"## 1. Watch: {WatchSeconds:0} s, nobody shoots");
            report.AppendLine();
            ReportCrows();
            report.AppendLine($"- Approaches started: **{CrowStats.Approaches}**; attack hits on the player: **{CrowStats.AttackHits}**; " +
                              $"damage taken: **{damageTaken:0} HP**.");
            report.AppendLine($"- Thefts: **{CrowStats.TheftsAttempted}** attempted, **{CrowStats.TheftsSucceeded}** delivered to a stash.");
            report.AppendLine($"- Stuck (moved under {StuckDistance} m in {StuckSeconds:0} s while it should fly): " +
                              $"**{stuck.Count}**{(stuckNotes.Count > 0 ? " — " + string.Join("; ", stuckNotes) : "")}.");
            report.AppendLine($"- Touching level geometry at any sample: **{insideGeometry.Count}**" +
                              $"{(insideNotes.Count > 0 ? " — " + string.Join("; ", insideNotes) : "")}.");
            report.AppendLine();

            // ---------- Fight ----------
            int funeralsBefore = CrowStats.FuneralAttendances, enragesBefore = CrowStats.Enrages;
            int evasionsBefore = CrowStats.Evasions;
            float damageBefore = damageTaken;
            FieldInfo shotEvent = typeof(PlayerShooting).GetField("ShotFired", BindingFlags.NonPublic | BindingFlags.Instance);
            var shooting = health.GetComponent<PlayerShooting>();
            System.Action<Vector3, Vector3, IDamageable> Fire() =>
                shotEvent?.GetValue(shooting) as System.Action<Vector3, Vector3, IDamageable>;

            float fightStart = Time.time;
            int shots = 0, crowShots = 0, misses = 0, inMagazineSniper = 0, inMagazineRifle = 0;
            float nextShot = Time.time + AimTime;
            BirdHealth target = null;
            bool cleared = false, ended = false;
            System.Action<int, bool, int> onEnded = (w, c, b) => { cleared = c; ended = true; };
            waves.WaveEnded += onEnded;
            while (!ended && Time.time - fightStart < FightLimit)
            {
                if (target == null || !target.IsAlive)
                {
                    target = FindObjectsByType<BirdHealth>().Where(b => b.IsAlive)
                        .OrderBy(b => (b.transform.position - health.transform.position).sqrMagnitude).FirstOrDefault();
                    nextShot = Time.time + AimTime;
                }

                if (target != null && Time.time >= nextShot)
                {
                    bool isCrow = target.GetComponent<CrowBrain>() != null;
                    WeaponData gun = isCrow ? sniper : rifle;
                    Vector3 origin = health.transform.position + Vector3.up * 1.4f;
                    shots++;
                    if (isCrow && ++crowShots % 3 == 0)
                    {
                        // A miss: the shot passes 1.5 m beside the crow.
                        Vector3 side = Vector3.Cross(Vector3.up, target.transform.position - origin).normalized;
                        Fire()?.Invoke(origin, target.transform.position + side * 1.5f, null);
                        misses++;
                    }
                    else
                    {
                        Fire()?.Invoke(origin, target.transform.position, target);
                        target.TakeDamage(gun.Damage, gun.Rarity);
                    }

                    nextShot = Time.time + gun.FireInterval;
                    if (isCrow && ++inMagazineSniper >= gun.MagazineSize) { inMagazineSniper = 0; nextShot += gun.ReloadTime; }
                    if (!isCrow && ++inMagazineRifle >= gun.MagazineSize) { inMagazineRifle = 0; nextShot += gun.ReloadTime; }
                }

                yield return null;
            }
            waves.WaveEnded -= onEnded;

            report.AppendLine("## 2. Fight: sniper for crows, assault rifle for pigeons, every third crow shot misses");
            report.AppendLine();
            report.AppendLine($"- Result: **{(cleared ? "wave cleared" : ended ? "time up" : "not finished in the test limit")}** " +
                              $"in **{Time.time - fightStart:0} s** (wave limit {data.Duration:0} s, " +
                              $"{WatchSeconds:0} s of it used by the watch); birds killed: **{waves.Killed}** of {data.TotalBirdCount}.");
            report.AppendLine($"- Shots: {shots}, {crowShots} of them at crows ({misses} deliberate near misses). " +
                              $"Dodges: **{CrowStats.Evasions - evasionsBefore}**.");
            report.AppendLine($"- Funerals attended: **{CrowStats.FuneralAttendances - funeralsBefore}**; crows enraged: " +
                              $"**{CrowStats.Enrages - enragesBefore}**; thefts delivered overall: **{CrowStats.TheftsSucceeded}**, " +
                              $"recovered: **{CrowStats.TheftsRecovered}**.");
            report.AppendLine($"- Damage taken while fighting: **{damageTaken - damageBefore:0} HP** (healed to full {heals} times " +
                              "overall when below 40%).");
            report.AppendLine();
            report.AppendLine("## Crows seen during the whole wave");
            report.AppendLine();
            ReportCrows();

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log($"[CrowWave3] Finished — {ReportPath}");
            EditorApplication.isPlaying = false;
        }

        private void Update()
        {
            if (health != null && health.CurrentHP < health.MaxHP * 0.4f)
            {
                health.Heal(health.MaxHP);
                heals++;
            }

            foreach (CrowBrain crow in FindObjectsByType<CrowBrain>())
            {
                if (!seen.TryGetValue(crow, out HashSet<CrowState> states))
                    seen[crow] = states = new HashSet<CrowState>();
                states.Add(crow.State);
                if (!crow.enabled)
                    continue;

                Vector3 position = crow.transform.position;
                if (!lastMove.TryGetValue(crow, out (Vector3 position, float time) last) ||
                    (position - last.position).sqrMagnitude > StuckDistance * StuckDistance)
                {
                    lastMove[crow] = (position, Time.time);
                }
                else if (Time.time - last.time > StuckSeconds && ShouldFly(crow.State) && stuck.Add(crow))
                {
                    stuckNotes.Add($"{crow.name} in {crow.State} at {position:F0}");
                }

                if (Time.frameCount % 30 == 0 && InsideGeometry(crow, out string what) && insideGeometry.Add(crow))
                    insideNotes.Add($"{crow.State} touching {what} at {crow.transform.position:F1}");
            }
        }

        private static bool ShouldFly(CrowState state) =>
            state == CrowState.Patrol || state == CrowState.Approach || state == CrowState.Retreat || state == CrowState.Evade;

        private static bool InsideGeometry(CrowBrain crow, out string what)
        {
            foreach (Collider hit in Physics.OverlapSphere(crow.transform.position, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(crow.transform) && hit.GetComponentInParent<BirdHealth>() == null &&
                    hit.GetComponentInParent<PlayerHealth>() == null)
                {
                    what = hit.name;
                    return true;
                }
            what = null;
            return false;
        }

        private void ReportCrows()
        {
            report.AppendLine($"Crows spawned so far: **{seen.Count}**.");
            report.AppendLine();
            report.AppendLine("| Crow | States seen |");
            report.AppendLine("| --- | --- |");
            int i = 1;
            foreach (KeyValuePair<CrowBrain, HashSet<CrowState>> entry in seen)
                report.AppendLine($"| {i++} | {string.Join(", ", entry.Value)} |");
            report.AppendLine();
        }

        private static List<WeaponData> LoadGuns() =>
            AssetDatabase.FindAssets("t:WeaponData", new[] { WeaponFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid)))
                .ToList();
    }
}
#endif
