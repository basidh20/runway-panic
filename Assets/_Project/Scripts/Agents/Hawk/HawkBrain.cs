using UnityEngine;
using UnityEngine.Rendering;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// The hawk's brain: a two-level (hierarchical) state machine. The top level is the <see cref="HawkMode"/>
    /// (Hunt, Herd, Withdraw, Dead); Hunt has its own sub-states (<see cref="HuntPhase"/>). Every mode and phase has
    /// an enter and an exit step and its own timer, so later phases (perch scoring, weakness model, stoop, herding)
    /// only fill in those steps.
    /// <para>H1 skeleton: each Hunt phase lasts <c>skeletonPhaseDuration</c> and then moves to the next one, so the whole
    /// cycle can be seen on the state label; Herd and Withdraw (forced from the component's context menu) return to Hunt
    /// after the same time.</para>
    /// </summary>
    public sealed class HawkBrain : BirdBrain
    {
        private const string LogPrefix = "[HawkFSM]";

        // Cached label strings, indexed by the enums: CurrentStateName never allocates.
        private static readonly string[] HuntPhaseNames = BuildHuntPhaseNames();
        private static readonly string[] ModeNames = { "Hunt", "Herd", "Withdraw", "Dead" };

        [Tooltip("The hawk's strategy numbers (ScriptableObjects/Birds/Strategy/HawkStrategyConfig).")]
        [SerializeField] private HawkStrategyConfig config;
        [Tooltip("H1 skeleton only: seconds before each phase moves on to the next one. Replaced by real conditions from H2 on.")]
        [SerializeField, Min(0.1f)] private float skeletonPhaseDuration = 2f;
        [Tooltip("Write every mode and phase change to the Console / Editor.log (prefix [HawkFSM]).")]
        [SerializeField] private bool logTransitions = true;

        private HawkMode mode = HawkMode.Hunt;
        private HuntPhase phase = HuntPhase.SelectPerch;
        private float modeTime;
        private float phaseTime;
        private bool started;

        /// <inheritdoc />
        public override string CurrentStateName => mode == HawkMode.Hunt ? HuntPhaseNames[(int)phase] : ModeNames[(int)mode];

        /// <summary>The strategy numbers in use.</summary>
        public HawkStrategyConfig Config => config;

        /// <summary>Current top-level mode.</summary>
        public HawkMode Mode => mode;

        /// <summary>Current Hunt sub-state. Only meaningful while <see cref="Mode"/> is <see cref="HawkMode.Hunt"/>.</summary>
        public HuntPhase Phase => phase;

        /// <summary>Seconds of active gameplay spent in the current mode.</summary>
        public float ModeTime => modeTime;

        /// <summary>Seconds of active gameplay spent in the current Hunt phase.</summary>
        public float PhaseTime => phaseTime;

        protected override void Awake()
        {
            base.Awake();

            if (config == null)
            {
                Debug.LogError($"{nameof(HawkBrain)} on '{name}' has no {nameof(HawkStrategyConfig)} assigned.", this);
                enabled = false;
            }

            EnableShadows();
        }

        private void Start()
        {
            started = true;
            modeTime = 0f;
            EnterMode(mode, HuntPhase.SelectPerch);
        }

        private void Update()
        {
            // Timers only run during active gameplay, so pausing or the shop never counts as perch time.
            if (!IsGameplayActive)
                return;

            float dt = Time.deltaTime;
            modeTime += dt;
            if (mode == HawkMode.Hunt)
                phaseTime += dt;

            switch (mode)
            {
                case HawkMode.Hunt:
                    UpdateHunt();
                    break;
                case HawkMode.Herd:
                case HawkMode.Withdraw:
                    if (modeTime >= skeletonPhaseDuration)
                        ChangeMode(HawkMode.Hunt);
                    break;
            }
        }

        private void UpdateHunt()
        {
            if (phaseTime < skeletonPhaseDuration)
                return;

            // H1: walk through the whole attack cycle in order, then start again.
            HuntPhase next = phase == HuntPhase.Recover ? HuntPhase.SelectPerch : phase + 1;
            ChangePhase(next);
        }

        /// <summary>
        /// Leaves the current mode (its exit step, and the current phase's exit step when leaving Hunt) and enters
        /// <paramref name="next"/>. Entering Hunt starts at <paramref name="huntEntryPhase"/>, so e.g. Herd can jump
        /// straight into <see cref="HuntPhase.Stoop"/>. Dead is final: no change leaves it.
        /// </summary>
        private void ChangeMode(HawkMode next, HuntPhase huntEntryPhase = HuntPhase.SelectPerch)
        {
            if (mode == HawkMode.Dead)
                return;

            LogTransition(CurrentStateName, next == HawkMode.Hunt ? HuntPhaseNames[(int)huntEntryPhase] : ModeNames[(int)next], modeTime);
            ExitMode(mode);
            mode = next;
            modeTime = 0f;
            EnterMode(next, huntEntryPhase);
        }

        /// <summary>Moves to another Hunt sub-state (exit step of the old one, enter step of the new one). Hunt mode only.</summary>
        private void ChangePhase(HuntPhase next)
        {
            if (mode != HawkMode.Hunt)
            {
                Debug.LogWarning($"{LogPrefix} {name}: ChangePhase({next}) ignored in mode {mode}.", this);
                return;
            }

            LogTransition(HuntPhaseNames[(int)phase], HuntPhaseNames[(int)next], phaseTime);
            ExitPhase(phase);
            phase = next;
            phaseTime = 0f;
            EnterPhase(next);
        }

        private void EnterMode(HawkMode entered, HuntPhase huntEntryPhase)
        {
            switch (entered)
            {
                case HawkMode.Hunt:
                    phase = huntEntryPhase;
                    phaseTime = 0f;
                    EnterPhase(huntEntryPhase);
                    break;
                case HawkMode.Herd:
                    Anim.SetFlying(true);
                    break;
                case HawkMode.Withdraw:
                    Anim.SetFlying(true);
                    break;
                case HawkMode.Dead:
                    break;
            }
        }

        private void ExitMode(HawkMode exited)
        {
            switch (exited)
            {
                case HawkMode.Hunt:
                    ExitPhase(phase);
                    // Leaving Hunt mid-attack (e.g. hit during the approach) must not keep the dive speed.
                    Motor.SpeedMultiplier = 1f;
                    break;
                case HawkMode.Herd:
                    break;
                case HawkMode.Withdraw:
                    break;
            }
        }

        private void EnterPhase(HuntPhase entered)
        {
            switch (entered)
            {
                case HuntPhase.SelectPerch:
                case HuntPhase.FlyToPerch:
                case HuntPhase.ApproachStoop:
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.Perched:
                    Anim.SetFlying(false);
                    break;
                case HuntPhase.Scream:
                    Anim.SetWingsSpread(0f);
                    break;
                case HuntPhase.Feint:
                case HuntPhase.Stoop:
                    Anim.SetDiving(true);
                    break;
                case HuntPhase.Recover:
                    Motor.SpeedMultiplier = 1f;
                    Anim.SetWingsSpread(1f);
                    break;
            }
        }

        private void ExitPhase(HuntPhase exited)
        {
            switch (exited)
            {
                case HuntPhase.Perched:
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.Feint:
                case HuntPhase.Stoop:
                    Anim.SetDiving(false);
                    break;
            }
        }

        /// <inheritdoc />
        protected override void OnBirdDied()
        {
            ChangeMode(HawkMode.Dead);
            base.OnBirdDied();
        }

        // The hawk's shadow sweeping over the ground is its fairness tell before a stoop, so it must always cast one.
        // Runtime only: the prefab asset is not changed. The state label's text is skipped (it must not cast a shadow).
        private void EnableShadows()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i].GetComponent<TextMesh>() == null)
                    renderers[i].shadowCastingMode = ShadowCastingMode.On;
        }

        private void LogTransition(string from, string to, float after)
        {
            if (logTransitions && started)
                Debug.Log($"{LogPrefix} {name} {from} -> {to} after {after:0.00}s", this);
        }

        private static string[] BuildHuntPhaseNames()
        {
            var names = new string[(int)HuntPhase.Recover + 1];
            for (int i = 0; i < names.Length; i++)
                names[i] = "Hunt/" + (HuntPhase)i;
            return names;
        }

        // Test hooks for the two-level machine (right-click the component in Play mode). Removed or kept for the viva in H7.
        [ContextMenu("Hawk FSM/Force Hunt")]
        private void ForceHunt()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Hunt);
        }

        [ContextMenu("Hawk FSM/Force Herd")]
        private void ForceHerd()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Herd);
        }

        [ContextMenu("Hawk FSM/Force Withdraw")]
        private void ForceWithdraw()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Withdraw);
        }

        [ContextMenu("Hawk FSM/Force Hunt Stoop (herd strike)")]
        private void ForceHuntStoop()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Hunt, HuntPhase.Stoop);
        }
    }
}
