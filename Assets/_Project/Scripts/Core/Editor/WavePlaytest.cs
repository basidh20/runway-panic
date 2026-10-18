using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RunwayPanic.Agents;
using RunwayPanic.Data;
using RunwayPanic.Player;
using RunwayPanic.UI;
using RunwayPanic.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.Core.EditorTools
{
    /// <summary>
    /// Tools > S2 > Run Wave and Checkpoint Playtest: plays <c>Main</c> by itself and writes
    /// <c>Docs/SystemsEngineer/WaveBalanceReport.md</c>.
    /// <list type="number">
    /// <item><b>Checkpoint</b>: walks the player into a checkpoint, dies, and checks the respawn point, the lost life
    /// and full health.</item>
    /// <item><b>Lives</b>: dies until the lives run out and checks Game Over, its screen and the lost sound; then
    /// Restart.</item>
    /// <item><b>Waves</b>: plays all four waves as a perfect-aim player: the real gun stats (damage, fire rate,
    /// magazine, reload), the right gun when owned, a short aim time per target; between waves buys the next wave's
    /// gun (and a medkit with spare coins). Records times, kills, coins and purchases. The player is healed when low so
    /// the run finishes; damage taken is reported.</item>
    /// </list>
    /// Runs at 4× speed. Main is never saved.
    /// </summary>
    public static class WavePlaytest
    {
        private const string MainScene = "Assets/_Project/Scenes/Main.unity";
        private const string ReportPath = "Docs/SystemsEngineer/WaveBalanceReport.md";
        private const string RunningKey = "RunwayPanic.WavePlaytest.Running";
        private const float Speed = 4f;
        private const float AimTime = 0.6f;

        private static IEnumerator routine;
        private static object waiting;
        private static float waitUntil;
        private static readonly List<string> checks = new List<string>();
        private static readonly StringBuilder waveLog = new StringBuilder();
        private static int passed, failed;

        [MenuItem("Tools/S2/Run Wave and Checkpoint Playtest")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Start();
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
                    Start();
                }
                else if (state == PlayModeStateChange.ExitingPlayMode && routine != null)
                {
                    Stop("Play mode ended before the playtest finished.");
                }
            };
        }

        private static void Start()
        {
            checks.Clear();
            waveLog.Clear();
            passed = failed = 0;
            routine = Playtest();
            waiting = null;
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
            Debug.Log("[Playtest] Started: checkpoint, lives, then all four waves.");
        }

        // A tiny coroutine runner on the editor loop: steps yield seconds (real time) or a Func<bool> to wait for.
        private static void Step()
        {
            if (!EditorApplication.isPlaying || routine == null)
                return;

            if (waiting is Func<bool> condition && !condition())
                return;
            if (waiting is float && Time.realtimeSinceStartup < waitUntil)
                return;

            KeepSpeed();
            try
            {
                if (!routine.MoveNext())
                {
                    Finish();
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Stop("Exception: " + e.Message);
                return;
            }

            waiting = routine.Current;
            if (waiting is float seconds)
                waitUntil = Time.realtimeSinceStartup + seconds;
        }

        private static void KeepSpeed()
        {
            GameManager game = GameManager.Instance;
            if (game != null && game.CurrentState == GameState.Playing)
                Time.timeScale = Speed;
        }

        private static void Check(bool ok, string what)
        {
            checks.Add($"| {(ok ? "PASS" : "**FAIL**")} | {what} |");
            if (ok) passed++; else failed++;
            Debug.Log($"[Playtest] {(ok ? "PASS" : "FAIL")}: {what}");
        }

        // =====================================================================

        private static IEnumerator Playtest()
        {
            yield return Until(() => GameManager.Instance != null && WaveManager.Instance != null && FindPlayer() != null);
            yield return 1f;

            foreach (IEnumerator part in new[] { CheckpointPart(), LivesPart(), WavesPart() })
                while (part.MoveNext())
                    yield return part.Current;
        }

        private static IEnumerator CheckpointPart()
        {
            PlayerHealth health = FindPlayer();
            var lives = health.GetComponent<PlayerLifeManager>();
            var respawn = health.GetComponent<PlayerRespawn>();
            Checkpoint[] checkpoints = UnityEngine.Object.FindObjectsByType<Checkpoint>();
            Check(checkpoints.Length > 0, $"Main has checkpoints ({checkpoints.Length})");
            if (checkpoints.Length == 0)
                yield break;

            Checkpoint checkpoint = checkpoints[0];
            Teleport(health.transform, checkpoint.transform.position + Vector3.up * 0.1f);
            yield return 0.6f;
            Check(respawn.CurrentCheckpoint == checkpoint.transform,
                $"walking into {checkpoint.name} makes it the respawn point");

            Teleport(health.transform, checkpoint.transform.position + new Vector3(12f, 0.1f, 0f));
            yield return 0.3f;
            int livesBefore = lives.CurrentLives;
            bool respawned = false;
            Action onRespawn = () => respawned = true;
            respawn.Respawned += onRespawn;
            health.TakeDamage(health.MaxHP * 10f, WeaponRarity.Common);
            float timeout = Time.realtimeSinceStartup + 10f;
            yield return Until(() => respawned || Time.realtimeSinceStartup > timeout);
            respawn.Respawned -= onRespawn;
            yield return 0.3f;

            float distance = Vector3.Distance(health.transform.position, checkpoint.transform.position);
            Check(respawned, "the player respawns after dying");
            Check(lives.CurrentLives == livesBefore - 1, $"a death costs one life ({livesBefore} → {lives.CurrentLives})");
            Check(distance < 3f, $"the player respawns at the checkpoint ({distance:0.0} m away)");
            Check(Mathf.Approximately(health.CurrentHP, health.MaxHP), "health is full after respawning");
        }

        private static IEnumerator LivesPart()
        {
            PlayerHealth health = FindPlayer();
            var lives = health.GetComponent<PlayerLifeManager>();
            int guard = 0;
            while (GameManager.Instance.CurrentState != GameState.GameOver && guard++ < 6)
            {
                yield return Until(() => health.IsAlive || GameManager.Instance.CurrentState == GameState.GameOver);
                if (GameManager.Instance.CurrentState == GameState.GameOver)
                    break;
                yield return 0.3f;
                health.TakeDamage(health.MaxHP * 10f, WeaponRarity.Common);
                yield return 0.5f;
            }

            yield return 0.5f;
            var screens = UnityEngine.Object.FindAnyObjectByType<MenuScreensUI>();
            var gameOverScreen = (GameObject)Field(screens, "gameOverScreen");
            Check(GameManager.Instance.CurrentState == GameState.GameOver,
                $"losing the last life ends the game (lives {lives.CurrentLives})");
            Check(gameOverScreen != null && gameOverScreen.activeInHierarchy, "the Game Over screen is shown");
            Check(CountClip("SFX_Game_Over") > 0, "the lost sound plays with the Game Over screen");

            // Restart from the Game Over screen, like a player would.
            Button restart = ((Button[])Field(screens, "restartButtons")).First(b => b.gameObject.activeInHierarchy);
            restart.onClick.Invoke();
            yield return 1f;
            yield return Until(() => GameManager.Instance != null && WaveManager.Instance != null && FindPlayer() != null);
            yield return 0.5f;
            Check(GameManager.Instance.CurrentState == GameState.Playing &&
                  FindPlayer().GetComponent<PlayerLifeManager>().CurrentLives == 3,
                "Restart starts a fresh game with 3 lives");
        }

        private static IEnumerator WavesPart()
        {
            GameManager game = GameManager.Instance;
            WaveManager waves = WaveManager.Instance;
            PlayerHealth health = FindPlayer();
            CoinManager coins = CoinManager.Instance;
            var inventory = health.GetComponent<PlayerInventory>();
            float damageTaken = 0f;
            int heals = 0;
            Action<float> onDamaged = amount => damageTaken += amount;
            health.Damaged += onDamaged;

            waveLog.AppendLine("| Wave | Birds | Killed | Time used / limit | Result | Coins before → after | Bought in the shop |");
            waveLog.AppendLine("| --- | ---: | ---: | --- | --- | --- | --- |");

            for (int wave = 1; wave <= waves.Waves.Count; wave++)
            {
                WaveData data = waves.Waves[wave - 1];
                int target = wave;
                yield return Until(() => game.CurrentState == GameState.GameOver ||
                                         (game.IsWaveActive && game.CurrentWave == target &&
                                          waves.Phase == WaveManager.WavePhase.Running));
                if (game.CurrentState == GameState.GameOver)
                    break;

                int coinsBefore = coins.Coins;
                float start = Time.time;
                bool cleared = false, ended = false;
                Action<int, bool, int> onEnded = (w, c, b) => { cleared = c; ended = true; };
                waves.WaveEnded += onEnded;

                // The perfect-aim player: one target at a time, aim, then fire at the gun's own rate until it dies.
                float nextShot = Time.time + AimTime;
                int shotsInMagazine = 0;
                BirdHealth current = null;
                while (!ended && game.CurrentState != GameState.GameOver)
                {
                    if (health.CurrentHP < health.MaxHP * 0.4f)
                    {
                        health.Heal(health.MaxHP);
                        heals++;
                    }

                    if (current == null || !current.IsAlive)
                    {
                        current = UnityEngine.Object.FindObjectsByType<BirdHealth>().FirstOrDefault(b => b.IsAlive);
                        nextShot = Time.time + AimTime;
                    }

                    if (current != null && Time.time >= nextShot)
                    {
                        WeaponData gun = BestGun(inventory, current.Species);
                        current.TakeDamage(gun.Damage, gun.Rarity);
                        shotsInMagazine++;
                        nextShot = Time.time + gun.FireInterval;
                        if (shotsInMagazine >= gun.MagazineSize)
                        {
                            shotsInMagazine = 0;
                            nextShot += gun.ReloadTime;
                        }
                    }

                    yield return null;
                }

                waves.WaveEnded -= onEnded;
                float used = Time.time - start;
                int killed = waves.Killed;
                yield return Until(() => game.CurrentState == GameState.Shopping ||
                                         game.CurrentState == GameState.Victory ||
                                         game.CurrentState == GameState.GameOver);
                int coinsAfter = coins.Coins;

                string bought = "—";
                if (game.CurrentState == GameState.Shopping && wave < waves.Waves.Count)
                {
                    bought = Shop(waves.Waves[wave]);
                    yield return 0.3f;
                    ShopManager.Instance.ContinueToNextWave();
                }

                string result = cleared ? $"cleared (+{data.ClearBonus})" : "time up";
                waveLog.AppendLine($"| {wave} · {data.Title} | {data.TotalBirdCount} | {killed} | " +
                                   $"{used:0}s / {data.Duration:0}s | {result} | {coinsBefore} → {coinsAfter} | {bought} |");
            }

            health.Damaged -= onDamaged;
            yield return 1f;
            Check(game.CurrentState == GameState.Victory, "clearing all four waves wins the game");
            waveLog.AppendLine();
            waveLog.AppendLine($"Damage the player took over the four waves: **{damageTaken:0} HP** " +
                               $"(healed to full {heals} times when below 40%).");
        }

        // Buys the gun for the next wave's main species if affordable, then a medkit with what is left over.
        private static string Shop(WaveData next)
        {
            ShopManager shop = ShopManager.Instance;
            var bought = new List<string>();
            ShopItem gun = GunFor(shop, next);
            if (gun != null)
            {
                ShopManager.PurchaseResult result = shop.TryBuy(gun);
                bought.Add(result == ShopManager.PurchaseResult.Success ? gun.DisplayName : $"{gun.DisplayName}: {result}");
            }

            ShopItem medkit = shop.Items.FirstOrDefault(i => i.ItemKind == ShopItem.Kind.Item && i.Item == ItemType.Medkit);
            if (medkit != null && shop.CheckPurchase(medkit) == ShopManager.PurchaseResult.Success &&
                shop.TryBuy(medkit) == ShopManager.PurchaseResult.Success)
                bought.Add(medkit.DisplayName);
            return string.Join(", ", bought);
        }

        // The shop's gun made for a wave's first (main) species.
        private static ShopItem GunFor(ShopManager shop, WaveData wave)
        {
            BirdSpecies wanted = wave.SpawnGroups[0].species.Species;
            return shop.Items.FirstOrDefault(i =>
                i.ItemKind == ShopItem.Kind.Weapon && i.Weapon != null && i.Weapon.TargetSpecies == wanted);
        }

        // The owned gun made for this species, else the owned gun doing most damage to it.
        private static WeaponData BestGun(PlayerInventory inventory, BirdSpeciesData species)
        {
            var owned = new List<WeaponData>();
            foreach (GameObject prefab in inventory.GetComponent<WeaponSwitcher>().OwnedWeapons)
                if (prefab != null && prefab.TryGetComponent(out WeaponBase weapon) && weapon.Data != null)
                    owned.Add(weapon.Data);
            WeaponData match = owned.FirstOrDefault(w => species != null && w.TargetSpecies == species.Species);
            if (match != null)
                return match;
            return owned.OrderByDescending(w => species != null ? species.GetDamageTaken(w.Damage, w.Rarity) : w.Damage)
                .First();
        }

        // =====================================================================

        private static void Finish()
        {
            EditorApplication.update -= Step;
            routine = null;
            Time.timeScale = 1f;
            WriteReport(null);
            Debug.Log($"[Playtest] Finished: {passed} passed, {failed} failed. Report: {ReportPath}");
            EditorApplication.isPlaying = false;
        }

        private static void Stop(string reason)
        {
            EditorApplication.update -= Step;
            routine = null;
            Time.timeScale = 1f;
            Check(false, reason);
            WriteReport(reason);
            Debug.LogError($"[Playtest] Stopped: {reason}");
        }

        private static void WriteReport(string stopped)
        {
            var report = new StringBuilder();
            report.AppendLine("# Wave balance and checkpoint playtest");
            report.AppendLine();
            report.AppendLine($"Written by **Tools > S2 > Run Wave and Checkpoint Playtest** on {DateTime.Now:yyyy-MM-dd HH:mm} " +
                              $"(Unity {Application.unityVersion}). Scene: `Main` (not saved). " +
                              $"Result: **{passed} passed, {failed} failed**" +
                              (stopped != null ? $" — stopped early: {stopped}" : "") + ".");
            report.AppendLine();
            report.AppendLine("## Checks");
            report.AppendLine();
            report.AppendLine("| Result | Check |");
            report.AppendLine("| --- | --- |");
            foreach (string line in checks)
                report.AppendLine(line);
            report.AppendLine();
            report.AppendLine("## Four waves, played by a perfect-aim player");
            report.AppendLine();
            report.AppendLine($"Real gun stats (damage, fire rate, magazine, reload), {AimTime}s to aim at each new bird, the " +
                              "right gun when owned. Between waves it buys the gun for the next wave's main species, then a " +
                              "medkit if coins are left. This is the best case: a real player misses and moves, so expect " +
                              "more time used and fewer kills.");
            report.AppendLine();
            report.Append(waveLog);
            report.AppendLine();
            AppendEconomy(report);

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
        }

        private static void AppendEconomy(StringBuilder report)
        {
            report.AppendLine("## Economy plan (from the wave assets)");
            report.AppendLine();
            report.AppendLine("| Wave | Birds | Every kill + clear bonus | ~60% of kills, no bonus | Next wave's gun |");
            report.AppendLine("| --- | --- | ---: | ---: | --- |");
            WaveManager waves = WaveManager.Instance;
            ShopManager shop = ShopManager.Instance;
            if (waves != null)
                for (int i = 0; i < waves.Waves.Count; i++)
                {
                    WaveData data = waves.Waves[i];
                    string birds = string.Join(" + ",
                        data.SpawnGroups.Where(g => g.species != null).Select(g => $"{g.count} {g.species.SpeciesName}"));
                    string next = "— (final wave)";
                    ShopItem gun = i + 1 < waves.Waves.Count && shop != null ? GunFor(shop, waves.Waves[i + 1]) : null;
                    if (gun != null)
                        next = $"{gun.DisplayName} ({gun.Price})";
                    report.AppendLine($"| {i + 1} · {data.Title} | {birds} | {data.TotalKillCoins + data.ClearBonus} | " +
                                      $"{Mathf.FloorToInt(data.TotalKillCoins * 0.6f)} | {next} |");
                }
            report.AppendLine();
            report.AppendLine("Coins carry over, so a weaker wave is made up by the next. Gulls and pigeons have no species " +
                              "brain yet (their owners are building them), so in this run they do not attack; crows and hawks do.");
        }

        // =====================================================================

        private static Func<bool> Until(Func<bool> condition) => condition;

        private static PlayerHealth FindPlayer() => UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();

        private static void Teleport(Transform player, Vector3 position)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.position = position;
            if (controller != null) controller.enabled = true;
        }

        private static int CountClip(string clip)
        {
            GameObject pool = GameObject.Find("[SoundPlayer]");
            if (pool == null)
                return 0;
            return pool.GetComponentsInChildren<AudioSource>(false)
                .Count(s => s.isPlaying && s.clip != null && s.clip.name == clip);
        }

        private static object Field(object target, string name) =>
            target.GetType()
                .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(target);
    }
}
