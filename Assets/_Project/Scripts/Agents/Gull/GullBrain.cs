using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>G1 skeleton only: displays the planned states, without simulating completed combat behaviour.</summary>
    [DisallowMultipleComponent]
    public sealed class GullBrain : BirdBrain
    {
        private const float PreviewStateSeconds = 2f;
        // BirdStateLabel compares string references: never allocate state.ToString() every frame.
        private static readonly string[] Labels =
        {
            "Idle", "GatherFod", "JoinKettle", "InKettle", "DescendToDrop",
            "Telegraph", "Drop", "ClimbToTop", "DistressDive", "Dead"
        };

        [Tooltip("Shared Gull strategy asset. Health, speed and attack stats still come from the species asset.")]
        [SerializeField] private GullStrategyConfig config;
        [Tooltip("TEMPORARY G1 demonstration: cycle living state labels every two gameplay seconds, without moving or attacking. Disable to stay Idle. Replaced by real transitions in G2–G5.")]
        [SerializeField] private bool previewStateCycle = true;

        private GullState state = GullState.Idle;
        private float stateTime;

        public GullStrategyConfig Config => config;
        public GullState State => state;
        public float StateTime => stateTime;
        public override string CurrentStateName => Labels[(int)state];

        private void Start()
        {
            if (config == null || Species == null || Species.Species != BirdSpecies.Gull)
            {
                Debug.LogError("GullBrain needs its Gull strategy config and the Gull species asset. Brain disabled.", this);
                enabled = false;
                return;
            }
            if (!Health.IsAlive) { OnBirdDied(); return; }
            OnEnter(state);
        }

        private void Update()
        {
            if (!IsGameplayActive || !Health.IsAlive || state == GullState.Dead || !previewStateCycle) return;
            stateTime += Time.deltaTime;
            if (stateTime < PreviewStateSeconds) return;

            // Demonstration order, NOT the final strategy's transition rules. Dead is entered only by a real death.
            switch (state)
            {
                case GullState.Idle: ChangeState(GullState.GatherFod); break;
                case GullState.GatherFod: ChangeState(GullState.JoinKettle); break;
                case GullState.JoinKettle: ChangeState(GullState.InKettle); break;
                case GullState.InKettle: ChangeState(GullState.DescendToDrop); break;
                case GullState.DescendToDrop: ChangeState(GullState.Telegraph); break;
                case GullState.Telegraph: ChangeState(GullState.Drop); break;
                case GullState.Drop: ChangeState(GullState.ClimbToTop); break;
                case GullState.ClimbToTop: ChangeState(GullState.DistressDive); break;
                case GullState.DistressDive: ChangeState(GullState.GatherFod); break;
            }
        }

        private void ChangeState(GullState next)
        {
            if (state == GullState.Dead || state == next) return;
            OnExit(state);
            state = next;
            stateTime = 0f;
            OnEnter(next);
        }

        private void OnEnter(GullState entered)
        {
            switch (entered)
            {
                case GullState.Idle:
                case GullState.Dead:
                    Motor?.Stop();
                    break;
                case GullState.GatherFod:
                case GullState.JoinKettle:
                case GullState.InKettle:
                case GullState.DescendToDrop:
                case GullState.Telegraph:
                case GullState.Drop:
                case GullState.ClimbToTop:
                case GullState.DistressDive:
                    // Runtime actions are deliberately left for G2–G5.
                    break;
            }
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.Log("[GullG1] " + name + " -> " + CurrentStateName + " (state-label preview only)", this);
        }

        private void OnExit(GullState exited)
        {
            switch (exited)
            {
                case GullState.DistressDive:
                    if (Motor != null) Motor.SpeedMultiplier = 1f;
                    break;
                // FOD claims, kettle membership and token cleanup are added alongside those systems.
            }
        }

        protected override void OnBirdDied()
        {
            ChangeState(GullState.Dead);
            base.OnBirdDied(); // Unsubscribes through OnDisable; the shared health/motor own the death fall.
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Motor != null) { Motor.SpeedMultiplier = 1f; Motor.Stop(); }
        }
    }
}
