using System;
using System.Collections.Generic;
using RunwayPanic.Agents;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Core
{
    /// <summary>
    /// Runs the timed waves (on the GameManager prefab). Before each wave a short countdown; then
    /// <see cref="GameManager.BeginWave"/>, and the wave's birds appear one by one at the level's
    /// <see cref="BirdPointType.Spawn"/> markers (away from the player, never inside a wall), with a cap on how many are
    /// alive at once. The wave ends when every bird is dead (the clear bonus is paid) or when the timer runs out (the
    /// survivors fly off, no coins); after a short "cleared / time up" moment <see cref="GameManager.CompleteWave"/> opens
    /// the shop, or wins after the last wave. Closing the shop starts the next countdown. Stops on game over. Runs on
    /// scaled time, so pausing freezes it.
    /// </summary>
    public sealed class WaveManager : MonoBehaviour
    {
        public enum WavePhase { Waiting, Countdown, Running, Ending, Finished }

        [Tooltip("One asset per wave, in order. Their count should match the GameManager's Total Waves.")]
        [SerializeField] private WaveData[] waves = Array.Empty<WaveData>();
        [Tooltip("Seconds before the first wave, to look around.")]
        [SerializeField, Min(0f)] private float firstCountdown = 6f;
        [Tooltip("Seconds between leaving the shop and the next wave.")]
        [SerializeField, Min(0f)] private float nextCountdown = 4f;
        [Tooltip("Seconds the \"wave cleared\" / \"time up\" moment lasts before the shop opens.")]
        [SerializeField, Min(0f)] private float endPause = 2.5f;

        [Header("Spawning")]
        [Tooltip("Only markers in this group are used (the airport's spawn markers). Empty = every Spawn marker.")]
        [SerializeField] private string spawnGroup = "AirportSpawn";
        [Tooltip("Markers closer than this to the player are skipped while any farther one exists.")]
        [SerializeField, Min(0f)] private float minPlayerDistance = 15f;
        [Tooltip("Clearance a spawned bird needs around it; positions overlapping anything are skipped.")]
        [SerializeField, Min(0.1f)] private float clearance = 0.6f;

        private readonly List<BirdHealth> alive = new List<BirdHealth>();
        private readonly List<BirdSpeciesData> queue = new List<BirdSpeciesData>();
        private readonly List<BirdPoint> points = new List<BirdPoint>();
        private GameManager game;
        private Transform player;
        private BirdPoint lastPoint;
        private float nextSpawnTime;
        private float waveTime;
        private float endTimer;
        private int killed;

        public static WaveManager Instance { get; private set; }

        /// <summary>A countdown began. Arguments: the coming wave's number and data.</summary>
        public event Action<int, WaveData> CountdownStarted;

        /// <summary>A wave ended. Arguments: wave number, true if every bird was killed, clear bonus paid.</summary>
        public event Action<int, bool, int> WaveEnded;

        public WavePhase Phase { get; private set; }

        /// <summary>Seconds left before the coming wave starts (Countdown phase).</summary>
        public float CountdownRemaining { get; private set; }

        /// <summary>Seconds left in the running wave.</summary>
        public float TimeRemaining { get; private set; }

        /// <summary>The coming or running wave's data (null before the first countdown).</summary>
        public WaveData CurrentData { get; private set; }

        /// <summary>Birds of the running wave not yet killed: alive now plus still to come.</summary>
        public int BirdsLeft => alive.Count + queue.Count;

        /// <summary>Birds of the running wave killed so far.</summary>
        public int Killed => killed;

        public IReadOnlyList<WaveData> Waves => waves;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"A second {nameof(WaveManager)} on '{name}' was disabled.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (game != null)
                game.WaveStarted -= HandleWaveStarted;
        }

        private void Update()
        {
            if (!Connect())
                return;

            switch (game.CurrentState)
            {
                case GameState.GameOver:
                case GameState.Victory:
                    Phase = WavePhase.Finished;
                    return;
                case GameState.Playing:
                    break;
                default:
                    return; // paused, shopping or in a menu: nothing moves
            }

            if (Phase == WavePhase.Ending)
                TickEnding();
            else if (game.IsWaveActive)
                RunWave();
            else if (Phase == WavePhase.Countdown)
                TickCountdown();
            else if (Phase == WavePhase.Waiting && game.CurrentWave < waves.Length)
                StartCountdown(game.CurrentWave == 0 ? firstCountdown : nextCountdown);
        }

        private bool Connect()
        {
            if (game != null)
                return true;
            if (GameManager.Instance == null)
                return false;

            game = GameManager.Instance;
            game.WaveStarted += HandleWaveStarted;
            if (waves.Length != game.TotalWaves)
                Debug.LogWarning($"[Waves] {waves.Length} wave assets, but the GameManager expects {game.TotalWaves}.",
                    this);
            return true;
        }

        // ---------- Countdown ----------

        private void StartCountdown(float seconds)
        {
            int wave = game.CurrentWave + 1;
            CurrentData = waves[wave - 1];
            CountdownRemaining = seconds;
            Phase = WavePhase.Countdown;
            CountdownStarted?.Invoke(wave, CurrentData);
        }

        private void TickCountdown()
        {
            CountdownRemaining -= Time.deltaTime;
            if (CountdownRemaining <= 0f)
            {
                CountdownRemaining = 0f;
                game.BeginWave(game.CurrentWave + 1);
            }
        }

        // ---------- Running wave ----------

        private void HandleWaveStarted(int wave)
        {
            CurrentData = waves[Mathf.Clamp(wave - 1, 0, waves.Length - 1)];
            Phase = WavePhase.Running;
            TimeRemaining = CurrentData.Duration;
            waveTime = 0f;
            killed = 0;
            nextSpawnTime = CurrentData.FirstSpawnDelay;
            alive.Clear();
            BuildQueue(CurrentData);
        }

        // Interleaves the groups (A, B, A, B, A, ...) so mixed waves stay mixed throughout.
        private void BuildQueue(WaveData data)
        {
            queue.Clear();
            int[] left = new int[data.SpawnGroups.Length];
            for (int i = 0; i < left.Length; i++)
                left[i] = data.SpawnGroups[i].species != null ? data.SpawnGroups[i].count : 0;

            bool added = true;
            while (added)
            {
                added = false;
                for (int i = 0; i < left.Length; i++)
                    if (left[i] > 0)
                    {
                        queue.Add(data.SpawnGroups[i].species);
                        left[i]--;
                        added = true;
                    }
            }
        }

        private void RunWave()
        {
            alive.RemoveAll(b => b == null); // removed by something else without dying
            waveTime += Time.deltaTime;
            TimeRemaining = Mathf.Max(0f, CurrentData.Duration - waveTime);

            if (queue.Count > 0 && waveTime >= nextSpawnTime && alive.Count < CurrentData.MaxAlive)
            {
                SpawnNext();
                nextSpawnTime = waveTime + CurrentData.SpawnInterval;
            }

            if (queue.Count == 0 && alive.Count == 0)
                EndWave(true);
            else if (TimeRemaining <= 0f)
                EndWave(false);
        }

        private void EndWave(bool cleared)
        {
            int wave = game.CurrentWave;
            int bonus = cleared ? CurrentData.ClearBonus : 0;
            if (bonus > 0 && CoinManager.Instance != null)
                CoinManager.Instance.AddCoins(bonus);

            // Time up: the survivors fly off (no coins for them).
            foreach (BirdHealth bird in alive)
                if (bird != null)
                {
                    bird.Died -= HandleBirdDied;
                    Destroy(bird.gameObject);
                }
            alive.Clear();
            queue.Clear();

            Phase = WavePhase.Ending;
            endTimer = endPause;
            WaveEnded?.Invoke(wave, cleared, bonus);
        }

        private void TickEnding()
        {
            endTimer -= Time.deltaTime;
            if (endTimer > 0f)
                return;
            Phase = WavePhase.Waiting;
            game.CompleteWave();
        }

        private void HandleBirdDied(BirdHealth bird)
        {
            bird.Died -= HandleBirdDied;
            if (alive.Remove(bird))
                killed++;
        }

        // ---------- Spawning ----------

        private void SpawnNext()
        {
            BirdSpeciesData species = queue[0];
            queue.RemoveAt(0);
            if (species.Prefab == null)
            {
                Debug.LogWarning($"[Waves] {species.name} has no prefab; skipped.", species);
                return;
            }

            Vector3 position = ChooseSpawnPosition();
            Vector3 toPlayer = PlayerPosition() - position;
            toPlayer.y = 0f;
            Quaternion rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;

            GameObject bird = Instantiate(species.Prefab, position, rotation);
            if (bird.TryGetComponent(out BirdHealth health))
            {
                health.Died += HandleBirdDied;
                alive.Add(health);
            }
        }

        private Vector3 ChooseSpawnPosition()
        {
            BirdPoint.GetAll(BirdPointType.Spawn, points);
            if (!string.IsNullOrEmpty(spawnGroup) && points.Exists(p => p.Group == spawnGroup))
                points.RemoveAll(p => p.Group != spawnGroup);
            if (points.Count == 0)
                return transform.position + Vector3.up * 8f;

            // Prefer markers away from the player, and not the one used last.
            Vector3 playerPosition = PlayerPosition();
            float minSqr = minPlayerDistance * minPlayerDistance;
            var candidates = points.FindAll(p => p != lastPoint && (p.Position - playerPosition).sqrMagnitude >= minSqr);
            if (candidates.Count == 0)
                candidates = points.FindAll(p => p != lastPoint);
            if (candidates.Count == 0)
                candidates = points;

            BirdPoint point = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            lastPoint = point;

            // Scatter inside the marker's radius, but only to a free spot (some markers sit near buildings).
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 candidate = point.Position + UnityEngine.Random.insideUnitSphere * point.Radius;
                if (!Physics.CheckSphere(candidate, clearance, ~0, QueryTriggerInteraction.Ignore))
                    return candidate;
            }
            return point.Position;
        }

        private Vector3 PlayerPosition()
        {
            if (player == null)
            {
                var controller = FindAnyObjectByType<PlayerController>();
                if (controller != null)
                    player = controller.transform;
            }
            return player != null ? player.position : Vector3.zero;
        }
    }
}
