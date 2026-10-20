using System.Collections.Generic;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>
    /// The scene's pigeon flock: one coordinator that turns individual pigeons into a group. It keeps the member list
    /// and runs the flock-level state machine (<see cref="PigeonFlockMode"/>); later phases add the shared decisions
    /// (gather point, rally point, dive tokens, roost slots) as partial files, the same way the brain is split.
    /// <para>
    /// Why a coordinator and not every bird deciding alone: the decisions that make a flock look like one organism
    /// (where to gather, who dives next, where to regroup) must be made <i>once</i> and seen by every member, and doing
    /// them once per flock instead of once per bird is also what keeps the cost flat as the flock grows.
    /// </para>
    /// Created on first use (a lazy singleton, like <c>GullKettle</c>), so <c>Main</c> needs no extra setup.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // decide for the flock before the members act on it in their own Update
    public sealed partial class PigeonFlock : MonoBehaviour
    {
        private static PigeonFlock instance;
        private static bool quitting;

        [Tooltip("Tuning asset. Taken from the first pigeon that joins when left empty.")]
        [SerializeField] private PigeonStrategyConfig config;

        private readonly List<PigeonBrain> members = new List<PigeonBrain>(12);
        private PigeonFlockMode mode = PigeonFlockMode.Roosting;
        private float modeTime;
        private GameManager boundGame;

        /// <summary>The scene's flock, created on first use in Play mode. Null outside Play mode or while quitting.</summary>
        public static PigeonFlock Instance
        {
            get
            {
                if (instance != null || quitting || !Application.isPlaying)
                    return instance;

                instance = FindAnyObjectByType<PigeonFlock>();
                if (instance == null)
                    instance = new GameObject("[PigeonFlock]").AddComponent<PigeonFlock>();
                return instance;
            }
        }

        /// <summary>Current members. Do not keep this across frames while iterating; birds come and go.</summary>
        public IReadOnlyList<PigeonBrain> Members => members;

        /// <summary>Number of current members.</summary>
        public int Count => members.Count;

        /// <summary>Current mode of the flock-level state machine.</summary>
        public PigeonFlockMode Mode => mode;

        /// <summary>Seconds spent in the current mode (frozen while paused or shopping).</summary>
        public float ModeTime => modeTime;

        /// <summary>The tuning asset (null until a pigeon has joined, unless assigned in the scene).</summary>
        public PigeonStrategyConfig Config => config;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning($"A second {nameof(PigeonFlock)} on '{name}' was destroyed; '{instance.name}' is already active.", this);
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void OnEnable()
        {
            BindGame();
        }

        private void OnDisable()
        {
            UnbindThreatEvents();
            UnbindGame();
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;

            members.Clear();
            instance = null;
        }

        private void OnApplicationQuit()
        {
            quitting = true;
        }

        /// <summary>True when <paramref name="bird"/> is a member.</summary>
        public bool Contains(PigeonBrain bird) => IndexOf(bird) >= 0;

        /// <summary>
        /// Adds a living pigeon. Returns false when the flock is full (the bird then fights alone). Joining twice is
        /// harmless. A bird may join in any mode: the mode decides what it does next.
        /// </summary>
        public bool Join(PigeonBrain bird)
        {
            if (!Participates(bird))
                return false;
            if (IndexOf(bird) >= 0)
                return true;

            if (config == null)
                config = bird.Config;
            if (config != null && members.Count >= config.maxFlockSize)
            {
                Log($"{bird.name} found the flock full ({members.Count}/{config.maxFlockSize}).");
                return false;
            }

            members.Add(bird);
            PigeonStats.Joins++;
            Log($"{bird.name} joined ({members.Count} members, mode {mode}).");
            OnMemberJoined(bird);

            // The first arrival wakes the flock up: scattered spawns start converging.
            if (mode == PigeonFlockMode.Roosting && members.Count == 1)
                ChangeMode(PigeonFlockMode.Gathering);
            return true;
        }

        /// <summary>
        /// Removes a member and frees everything the flock reserved for it (roost slot, dive token). Safe to call for a
        /// bird that is not a member.
        /// </summary>
        public void Leave(PigeonBrain bird)
        {
            int index = IndexOf(bird);
            if (index < 0)
                return;

            members.RemoveAt(index);
            ReleaseClaims(bird);
            PigeonStats.Leaves++;
            Log($"{(bird != null ? bird.name : "a destroyed pigeon")} left ({members.Count} members).");

            // Nobody left: the flock goes idle and starts fresh with the next arrival.
            if (members.Count == 0)
                ChangeMode(PigeonFlockMode.Roosting);
        }

        /// <summary>Leaves the current mode and enters <paramref name="next"/> (runs Exit then Enter, resets the mode timer).</summary>
        private void ChangeMode(PigeonFlockMode next)
        {
            if (next == mode)
                return;

            PigeonFlockMode previous = mode;
            ExitMode(previous);
            mode = next;
            modeTime = 0f;
            PigeonStats.FlockModeChanges++;
            Log($"mode {previous} -> {next} ({members.Count} members).");
            EnterMode(next);
        }

        private void Update()
        {
            BindGame();
            BindThreatEvents();
            PruneMembers();

            // Paused, shopping or game over: the flock freezes with its members.
            if (boundGame != null && boundGame.CurrentState != GameState.Playing)
                return;

            modeTime += Time.deltaTime;
            TickMode(mode);
        }

        // Safety net: a member destroyed or disabled without telling the flock (it always should, via its OnDisable).
        private void PruneMembers()
        {
            for (int i = members.Count - 1; i >= 0; i--)
                if (!Participates(members[i]))
                    Leave(members[i]);
        }

        // Identity search: a destroyed member compares equal to null under Unity's == and Equals, so List.IndexOf
        // could match the wrong destroyed entry.
        private int IndexOf(PigeonBrain bird)
        {
            for (int i = 0; i < members.Count; i++)
                if (ReferenceEquals(members[i], bird))
                    return i;
            return -1;
        }

        private static bool Participates(PigeonBrain bird) => bird != null && bird.isActiveAndEnabled && bird.IsAlive;

        // ---------- Game state ----------

        // A new wave is a fresh fight: flock-level memory (last attack side, rally history, tokens) must not carry over.
        private void HandleWaveStarted(int wave)
        {
            Log($"wave {wave} started: flock memory reset.");
            ResetForNewWave();
            ChangeMode(members.Count > 0 ? PigeonFlockMode.Gathering : PigeonFlockMode.Roosting);
        }

        // The WaveManager destroys surviving birds at time-up (they leave through their OnDisable); the flock only
        // drops what it decided for the finished wave.
        private void HandleWaveCompleted(int wave)
        {
            ResetForNewWave();
        }

        private void BindGame()
        {
            // The GameManager may appear after the flock (or be replaced on a scene reload): rebind when it changes.
            if (boundGame == GameManager.Instance)
                return;

            UnbindGame();
            boundGame = GameManager.Instance;
            if (boundGame != null)
            {
                boundGame.WaveStarted += HandleWaveStarted;
                boundGame.WaveCompleted += HandleWaveCompleted;
            }
        }

        private void UnbindGame()
        {
            if (boundGame != null)
            {
                boundGame.WaveStarted -= HandleWaveStarted;
                boundGame.WaveCompleted -= HandleWaveCompleted;
            }

            boundGame = null;
        }

        private void Log(string message)
        {
            // Debug.isDebugBuild is true in the Editor and development builds only.
            if (config != null && config.logFlockEvents && Debug.isDebugBuild)
                Debug.Log("[PigeonFlock] " + message, this);
        }

        // ---- Hooks implemented by the behaviour files (removed by the compiler when a file is absent) ----

        partial void OnMemberJoined(PigeonBrain bird);
        partial void ReleaseClaims(PigeonBrain bird);
        partial void ResetForNewWave();
        partial void EnterMode(PigeonFlockMode entered);
        partial void TickMode(PigeonFlockMode current);
        partial void ExitMode(PigeonFlockMode exited);

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            quitting = false;
        }
    }
}
