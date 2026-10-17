using System;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// The crow: "The Thief With a Grudge". A finite state machine (<see cref="CrowState"/>) that patrols,
    /// approaches the player around remembered danger, steals pickups, mourns its dead and holds vigils.
    /// <para>
    /// The class is split into partial files, one per behaviour (CrowBrain.Patrol.cs, CrowBrain.Approach.cs, ...).
    /// This core file owns the state machine itself: changing state, timing, and calling each state's
    /// Enter / Tick / Exit hook. A behaviour file implements the hooks for its own states; a hook with no
    /// implementation is removed by the compiler, so the brain works with any subset of behaviours.
    /// </para>
    /// </summary>
    public sealed partial class CrowBrain : BirdBrain
    {
        private static readonly string[] StateNames = Enum.GetNames(typeof(CrowState));
        private static readonly string[] CarryingStateNames = Array.ConvertAll(StateNames, s => s + " [carry]");

        [Tooltip("All tuning values for the crow strategy.")]
        [SerializeField] private CrowStrategyConfig config;

        private CrowState state = CrowState.Patrol;
        private float stateTime;
        private bool started;

        /// <summary>Current state of the state machine.</summary>
        public CrowState State => state;

        /// <summary>Seconds spent in the current state.</summary>
        public float StateTime => stateTime;

        /// <summary>The tuning asset.</summary>
        public CrowStrategyConfig Config => config;

        /// <summary>True while the crow holds a stolen pickup (set by the theft behaviour).</summary>
        public bool IsCarrying
        {
            get
            {
                bool carrying = false;
                CheckCarrying(ref carrying);
                return carrying;
            }
        }

        /// <inheritdoc />
        public override string CurrentStateName => (IsCarrying ? CarryingStateNames : StateNames)[(int)state];

        protected override void Awake()
        {
            base.Awake();
            if (config == null)
            {
                Debug.LogError($"{nameof(CrowBrain)} on '{name}' has no {nameof(CrowStrategyConfig)} assigned.", this);
                enabled = false;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SubscribeFuneral();
            SubscribeVigil();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            UnsubscribeFuneral();
            UnsubscribeVigil();
        }

        private void Start()
        {
            EnsureDangerMap();
            EnsureDebugOverlay();
            started = true;
            EnterState(state);
        }

        private void Update()
        {
            if (!started || state == CrowState.Dead)
                return;

            // Paused, shopping or no player yet: freeze decisions (the motor keeps its last order).
            if (!IsGameplayActive || Hub == null || !Hub.HasPlayer)
                return;

            stateTime += Time.deltaTime;

            UpdateVigilCheck();
            if (state == CrowState.Patrol || state == CrowState.Approach)
                UpdateTheftScan();
            if (state == CrowState.Patrol)
                UpdateDetection();

            TickState(state);
        }

        /// <summary>
        /// Leaves the current state and enters <paramref name="next"/> (runs Exit then Enter, resets the state timer).
        /// </summary>
        public void ChangeState(CrowState next)
        {
            if (state == CrowState.Dead)
                return;

            ExitState(state);
            state = next;
            stateTime = 0f;
            EnterState(next);
        }

        protected override void OnBirdDied()
        {
            OnDiedDropLoot();
            ChangeState(CrowState.Dead);
            base.OnBirdDied();
        }

        // Chest height on the player: the point crows dive at.
        private Vector3 PlayerChest => Hub.PlayerPosition + Vector3.up * 1.2f;

        private void EnterState(CrowState s)
        {
            switch (s)
            {
                case CrowState.Patrol: EnterPatrol(); break;
                case CrowState.Approach: EnterApproach(); break;
                case CrowState.Attack: EnterAttack(); break;
                case CrowState.Retreat: EnterRetreat(); break;
                case CrowState.StealFly: EnterStealFly(); break;
                case CrowState.CarryToStash: EnterCarryToStash(); break;
                case CrowState.Funeral: EnterFuneral(); break;
                case CrowState.Enraged: EnterEnraged(); break;
                case CrowState.Vigil: EnterVigil(); break;
                case CrowState.Dead: Motor.Stop(); break;
            }
        }

        private void TickState(CrowState s)
        {
            switch (s)
            {
                case CrowState.Patrol: TickPatrol(); break;
                case CrowState.Approach: TickApproach(); break;
                case CrowState.Attack: TickAttack(); break;
                case CrowState.Retreat: TickRetreat(); break;
                case CrowState.StealFly: TickStealFly(); break;
                case CrowState.CarryToStash: TickCarryToStash(); break;
                case CrowState.Funeral: TickFuneral(); break;
                case CrowState.Enraged: TickEnraged(); break;
                case CrowState.Vigil: TickVigil(); break;
            }
        }

        private void ExitState(CrowState s)
        {
            switch (s)
            {
                case CrowState.Funeral: ExitFuneral(); break;
                case CrowState.Enraged: ExitEnraged(); break;
                case CrowState.Vigil: ExitVigil(); break;
            }
        }

        // ---- Hooks implemented by the behaviour files (removed by the compiler when a file is absent) ----

        partial void EnterPatrol();
        partial void TickPatrol();

        partial void EnsureDangerMap();
        partial void EnsureDebugOverlay();

        partial void UpdateDetection();
        partial void EnterApproach();
        partial void TickApproach();

        partial void EnterAttack();
        partial void TickAttack();
        partial void EnterRetreat();
        partial void TickRetreat();

        partial void CheckCarrying(ref bool carrying);
        partial void UpdateTheftScan();
        partial void EnterStealFly();
        partial void TickStealFly();
        partial void EnterCarryToStash();
        partial void TickCarryToStash();
        partial void OnDiedDropLoot();

        partial void SubscribeFuneral();
        partial void UnsubscribeFuneral();
        partial void EnterFuneral();
        partial void TickFuneral();
        partial void ExitFuneral();

        partial void EnterEnraged();
        partial void TickEnraged();
        partial void ExitEnraged();

        partial void SubscribeVigil();
        partial void UnsubscribeVigil();
        partial void UpdateVigilCheck();
        partial void EnterVigil();
        partial void TickVigil();
        partial void ExitVigil();
    }
}
