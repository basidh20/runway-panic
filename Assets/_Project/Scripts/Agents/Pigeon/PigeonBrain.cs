using System;
using Unity.Profiling;
using UnityEngine;

namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>
    /// The pigeon: one member of "The Panicking Flock". Alone a pigeon is weak (20 HP, 4 m/s, slower than a walking
    /// player); the threat is the group. Decisions are split in two levels:
    /// <list type="bullet">
    /// <item><see cref="PigeonFlock"/> runs the flock-level state machine (<see cref="PigeonFlockMode"/>): where the group
    /// gathers, when it mobs, who may dive.</item>
    /// <item>This brain runs the bird's own state machine (<see cref="PigeonState"/>): how this bird takes part.</item>
    /// </list>
    /// <para>
    /// The class is split into partial files, one per behaviour (PigeonBrain.Join.cs, ...). This core file owns the
    /// state machine itself: changing state, timing, and calling each state's Enter / Tick / Exit hook. A behaviour
    /// file implements the hooks for its own states; a hook with no implementation is removed by the compiler, so the
    /// brain works with any subset of behaviours (a state without one simply keeps the motor's last order).
    /// </para>
    /// </summary>
    public sealed partial class PigeonBrain : BirdBrain
    {
        // Built once: CurrentStateName is read every frame by the label and must not allocate.
        private static readonly string[] StateNames = Enum.GetNames(typeof(PigeonState));

        // Profiler marker for the script-cost measurement: every pigeon's decisions, A* searches included.
        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("PigeonBrain.Update");

        [Tooltip("All tuning values for the pigeon strategy.")]
        [SerializeField] private PigeonStrategyConfig config;

        private PigeonState state = PigeonState.JoinFlock;
        private float stateTime;
        private bool started;

        /// <summary>Current state of the bird's state machine.</summary>
        public PigeonState State => state;

        /// <summary>Seconds spent in the current state (frozen while paused or shopping).</summary>
        public float StateTime => stateTime;

        /// <summary>The tuning asset.</summary>
        public PigeonStrategyConfig Config => config;

        /// <summary>False once the bird has died (or if it has no health component).</summary>
        public bool IsAlive => Health != null && Health.IsAlive;

        /// <summary>The bird's position (the motor's, so it matches what physics sees).</summary>
        public Vector3 Position => Motor != null ? Motor.Position : transform.position;

        /// <summary>The bird's current velocity in m/s.</summary>
        public Vector3 Velocity => Motor != null ? Motor.Velocity : Vector3.zero;

        /// <inheritdoc />
        public override string CurrentStateName => StateNames[(int)state];

        protected override void Awake()
        {
            base.Awake();
            if (config == null)
            {
                Debug.LogError($"{nameof(PigeonBrain)} on '{name}' has no {nameof(PigeonStrategyConfig)} assigned.", this);
                enabled = false;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Health != null) Health.Damaged += HandleDamage;

            // Re-enabled after Start (pooling, tests): start over by joining the flock again, because OnDisable left it.
            if (started && IsAlive)
            {
                state = PigeonState.JoinFlock;
                stateTime = 0f;
                EnterState(state);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Health != null) Health.Damaged -= HandleDamage;
            if (Motor != null && IsAlive) { Motor.SpeedMultiplier = 1f; Motor.Stop(); }

            // Also runs when the WaveManager destroys a surviving bird at time-up (no death event): without this the
            // flock would keep a dead reference holding a roost slot or a dive token.
            ReleaseFlockClaims();
        }

        private void OnDestroy()
        {
            // A living bird removed without dying is a despawn (time-up cleanup). OnDisable already left the flock for
            // an enabled brain; releasing again here covers a brain that was destroyed while disabled.
            if (started && state != PigeonState.Dead)
                PigeonStats.Despawns++;
            ReleaseFlockClaims();
        }

        private void Start()
        {
            started = true;
            EnterState(state);
        }

        private void Update()
        {
            using (UpdateMarker.Auto())
                Think();
        }

        private void Think()
        {
            if (!started || state == PigeonState.Dead)
                return;

            // The shared motor does not gate Shopping itself: stop explicitly, then reissue orders on resume.
            if (!IsGameplayActive)
            {
                if (Motor != null) Motor.Stop();
                nextPath = 0f;
                return;
            }

            if (Motor == null || config == null) return;
            if (Hub == null || !Hub.HasPlayer || !Hub.Health.IsAlive)
            {
                if (state != PigeonState.JoinFlock && state != PigeonState.FlyToRoost && state != PigeonState.Roosting)
                    ChangeState(PigeonState.FlyToRoost);
            }
            else if (state == PigeonState.Roosting || state == PigeonState.FlyToRoost)
                ChangeState(flock != null ? PigeonState.Flocking : PigeonState.Solo);

            if (flock != null && flock.Mode == PigeonFlockMode.Scattering && state != PigeonState.Panic)
                ChangeState(PigeonState.Panic);

            stateTime += Time.deltaTime;
            TickState(state);
        }

        /// <summary>
        /// Leaves the current state and enters <paramref name="next"/> (runs Exit then Enter, resets the state timer).
        /// </summary>
        public void ChangeState(PigeonState next)
        {
            if (state == PigeonState.Dead || state == next)
                return;

            ExitState(state);
            state = next;
            stateTime = 0f;
            EnterState(next);
        }

        protected override void OnBirdDied()
        {
            if (flock != null && config != null) flock.ReportThreat(Health.LastHitShotOrigin, config.deathAlarm);
            PigeonStats.Deaths++;
            ChangeState(PigeonState.Dead);
            base.OnBirdDied();
        }

        private void EnterState(PigeonState s)
        {
            switch (s)
            {
                case PigeonState.JoinFlock: EnterJoinFlock(); break;
                case PigeonState.FlyToRoost: EnterFlyToRoost(); break;
                case PigeonState.Roosting: EnterRoosting(); break;
                case PigeonState.Flocking: EnterFlocking(); break;
                case PigeonState.Dive: EnterDive(); break;
                case PigeonState.Recover: EnterRecover(); break;
                case PigeonState.Panic: EnterPanic(); break;
                case PigeonState.Solo: EnterSolo(); break;
                // BirdHealth has already stopped and disabled the motor; only the flock bookkeeping is left.
                case PigeonState.Dead: ReleaseFlockClaims(); break;
            }
        }

        private void TickState(PigeonState s)
        {
            switch (s)
            {
                case PigeonState.JoinFlock: TickJoinFlock(); break;
                case PigeonState.FlyToRoost: TickFlyToRoost(); break;
                case PigeonState.Roosting: TickRoosting(); break;
                case PigeonState.Flocking: TickFlocking(); break;
                case PigeonState.Dive: TickDive(); break;
                case PigeonState.Recover: TickRecover(); break;
                case PigeonState.Panic: TickPanic(); break;
                case PigeonState.Solo: TickSolo(); break;
            }
        }

        private void ExitState(PigeonState s)
        {
            switch (s)
            {
                case PigeonState.FlyToRoost: ExitFlyToRoost(); break;
                case PigeonState.Roosting: ExitRoosting(); break;
                case PigeonState.Flocking: ExitFlocking(); break;
                case PigeonState.Dive: ExitDive(); break;
                case PigeonState.Recover: ExitRecover(); break;
                case PigeonState.Panic: ExitPanic(); break;
                case PigeonState.Solo: ExitSolo(); break;
            }
        }

        // ---- Hooks implemented by the behaviour files (removed by the compiler when a file is absent) ----

        partial void EnterJoinFlock();
        partial void TickJoinFlock();

        partial void EnterFlyToRoost();
        partial void TickFlyToRoost();
        partial void ExitFlyToRoost();
        partial void EnterRoosting();
        partial void TickRoosting();
        partial void ExitRoosting();

        partial void EnterFlocking();
        partial void TickFlocking();
        partial void ExitFlocking();

        partial void EnterDive();
        partial void TickDive();
        partial void ExitDive();
        partial void EnterRecover();
        partial void TickRecover();
        partial void ExitRecover();

        partial void EnterPanic();
        partial void TickPanic();
        partial void ExitPanic();

        partial void EnterSolo();
        partial void TickSolo();
        partial void ExitSolo();
    }
}
