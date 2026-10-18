using System.Collections.Generic;
using RunwayPanic.AI;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>G2 collection through shared A*. JoinKettle waits for the G3 formation implementation.</summary>
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
        [Tooltip("Historical G1 label demonstration only. Leave OFF for real G2 gathering.")]
        [SerializeField] private bool previewStateCycle;

        private GullState state = GullState.Idle;
        private float stateTime;
        private readonly List<BirdPoint> fodMarkers = new List<BirdPoint>();
        private readonly HashSet<FodSpawnPoint> failedSupplies = new HashSet<FodSpawnPoint>();
        private FodSpawnPoint targetSupply;
        private FodItem carriedFod;
        private float noSupplyTime, nextRepath, travelTime;
        private bool started;

        public GullStrategyConfig Config => config;
        public GullState State => state;
        public float StateTime => stateTime;
        public FodItem CarriedFod => carriedFod;
        public FodSpawnPoint TargetSupply => targetSupply;
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
            started = true;
            OnEnter(state);
            if (!previewStateCycle) ChangeState(GullState.GatherFod);
        }

        private void Update()
        {
            if (!IsGameplayActive || !Health.IsAlive || state == GullState.Dead) return;
            stateTime += Time.deltaTime;
            if (!previewStateCycle)
            {
                if (state == GullState.GatherFod) GatherFod();
                return;
            }
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

        private void GatherFod()
        {
            if (carriedFod != null) { ChangeState(GullState.JoinKettle); return; }
            if (targetSupply != null && (!targetSupply.IsReady || targetSupply.Claimant != this)) ReleaseSupply();
            if (targetSupply == null)
            {
                BirdPoint.GetAll(BirdPointType.FodSpawn, fodMarkers);
                FodSpawnPoint nearest = null;
                float bestDistance = float.PositiveInfinity;
                foreach (var marker in fodMarkers)
                {
                    var supply = marker.GetComponent<FodSpawnPoint>();
                    if (supply == null || !supply.IsReady || supply.Claimant != null || failedSupplies.Contains(supply)) continue;
                    float distance = (supply.ApproachPosition - Motor.Position).sqrMagnitude;
                    if (distance < bestDistance) { nearest = supply; bestDistance = distance; }
                }
                if (nearest != null && nearest.TryClaim(this))
                {
                    targetSupply = nearest; noSupplyTime = travelTime = 0f; nextRepath = 0f;
                }
                else
                {
                    noSupplyTime += Time.deltaTime;
                    if (noSupplyTime >= 3f) ChangeState(GullState.JoinKettle);
                    return;
                }
            }

            travelTime += Time.deltaTime;
            Vector3 approach = targetSupply.ApproachPosition;
            float remaining = Vector3.Distance(Motor.Position, approach);
            var grid = FlightGrid.Instance;
            bool clearApproach = remaining < 4f && grid != null && grid.IsFlyable(approach) &&
                PathSmoother.IsClear(Motor.Position, approach, grid.SnapLineRadius, grid.ObstacleMask);
            if (remaining <= 1.15f && clearApproach && targetSupply.TryTake(this, out var item))
            {
                carriedFod = item;
                item.AttachTo(transform, new Vector3(0, -.15f, .05f));
                ChangeState(GullState.JoinKettle);
                return;
            }
            if (travelTime >= 15f)
            {
                failedSupplies.Add(targetSupply); ReleaseSupply(); Motor.Stop(); return;
            }
            // Slowing near the target avoids the shared motor's wide final-approach circles. No transform teleport.
            Motor.SpeedMultiplier = Mathf.Clamp(remaining / 4f, .15f, 1f);
            if (remaining < 4f && clearApproach) { Motor.FlyTo(approach); return; }
            if (Time.time < nextRepath) return;
            if (RequestPath(approach)) nextRepath = Time.time + Mathf.Max(.1f, config.repathInterval);
            else if (!LastRequestWasThrottled)
            {
                failedSupplies.Add(targetSupply); ReleaseSupply(); Motor.Stop();
            } // Throttled requests retain the claim/path and retry next frame.
        }

        private void ReleaseSupply()
        {
            if (targetSupply != null) targetSupply.ReleaseClaim(this);
            targetSupply = null;
            if (Motor != null) Motor.SpeedMultiplier = 1f;
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
                    noSupplyTime = travelTime = nextRepath = 0f;
                    failedSupplies.Clear();
                    break;
                case GullState.JoinKettle:
                    if (!previewStateCycle) Motor?.Stop(); // G3 adds the actual formation journey here.
                    break;
                case GullState.InKettle:
                case GullState.DescendToDrop:
                case GullState.Telegraph:
                case GullState.Drop:
                case GullState.ClimbToTop:
                case GullState.DistressDive:
                    // Runtime actions follow in G3–G5.
                    break;
            }
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.Log("[Gull] " + name + " -> " + CurrentStateName + (previewStateCycle ? " (G1 label preview)" : ""), this);
        }

        private void OnExit(GullState exited)
        {
            switch (exited)
            {
                case GullState.GatherFod:
                    ReleaseSupply();
                    break;
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
            ReleaseSupply();
            if (carriedFod != null) { carriedFod.Despawn(); carriedFod = null; }
            if (Motor != null) { Motor.SpeedMultiplier = 1f; Motor.Stop(); }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (started && state != GullState.Dead && !previewStateCycle)
            { ChangeState(GullState.GatherFod); stateTime = 0f; }
        }
    }
}
