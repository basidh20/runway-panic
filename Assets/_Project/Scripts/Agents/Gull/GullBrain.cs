using System;
using System.Collections.Generic;
using RunwayPanic.AI;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>Gull collection, token-controlled predictive bombing and damage-triggered group distress dive.</summary>
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
        [Tooltip("Optional final Gull warning call. An audible synthetic chirp is used until a clip is assigned.")]
        [SerializeField] private AudioClip callClip;

        private GullState state = GullState.Idle;
        private float stateTime;
        private readonly List<BirdPoint> fodMarkers = new List<BirdPoint>();
        private readonly HashSet<FodSpawnPoint> failedSupplies = new HashSet<FodSpawnPoint>();
        private FodSpawnPoint targetSupply;
        private FodItem carriedFod;
        private float noSupplyTime, nextRepath, travelTime;
        private bool started;
        private GullKettle kettle;
        private bool suspended;
        private Vector3 predictedPoint, attackTarget, swoopStart, swoopAim;
        private AudioSource warningSource;
        private AudioClip fallbackCall;
        private bool swooping;
        private readonly List<BirdHealth> distressNeighbours = new List<BirdHealth>();
        private float distressCooldownRemaining;
        private Vector3 distressAim, distressStart;

        public GullStrategyConfig Config => config;
        public GullState State => state;
        public float StateTime => stateTime;
        public FodItem CarriedFod => carriedFod;
        public FodSpawnPoint TargetSupply => targetSupply;
        public bool IsAlive => Health != null && Health.IsAlive;
        public Vector3 PredictedDropPoint => predictedPoint;
        public bool IsSwooping => swooping;
        /// <summary>Raised when aim locks and the audible/wing warning begins.</summary>
        public event Action<GullBrain> TelegraphStarted;
        /// <summary>Actual released piece and locked aim point; useful for G6 accuracy measurements.</summary>
        public event Action<FodItem, Vector3> FodDropped;
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
            if (!Health.IsAlive || state == GullState.Dead) return;
            if (!IsGameplayActive)
            {
                if (!suspended) { Motor.Stop(); suspended = true; }
                return;
            }
            if (suspended) { suspended = false; nextRepath = 0f; }
            distressCooldownRemaining = Mathf.Max(0f, distressCooldownRemaining - Time.deltaTime);
            stateTime += Time.deltaTime;
            if (!previewStateCycle)
            {
                switch (state)
                {
                    case GullState.GatherFod: GatherFod(); break;
                    case GullState.JoinKettle: JoinKettle(); break;
                    case GullState.InKettle: Orbit(); break;
                    case GullState.DescendToDrop: ApproachDrop(); break;
                    case GullState.Telegraph: Telegraph(); break;
                    case GullState.Drop: Drop(); break;
                    case GullState.ClimbToTop: ClimbToTop(); break;
                    case GullState.DistressDive: DistressDive(); break;
                }
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

        private void JoinKettle()
        {
            if (kettle == null) { kettle = GullKettle.Instance; if (kettle == null) return; kettle.Configure(config); }
            Vector3 target = kettle.JoinPosition;
            if (Vector3.Distance(Motor.Position, target) <= 3f)
            {
                if (kettle.Join(this)) ChangeState(GullState.InKettle);
                return;
            }
            if (Time.time < nextRepath) return;
            if (RequestPath(target) || !LastRequestWasThrottled) nextRepath = Time.time + Mathf.Max(.1f, config.repathInterval);
        }

        private void Orbit()
        {
            if (kettle == null || !kettle.Contains(this)) { ChangeState(carriedFod != null ? GullState.JoinKettle : GullState.GatherFod); return; }
            Motor.FlyTo(kettle.GetSlotPosition(this));
            if (stateTime >= .75f && kettle.TakeToken(this)) ChangeState(GullState.DescendToDrop);
        }

        private bool OwnsAttack()
        {
            if (kettle != null && kettle.TokenHolder == this && Hub != null && Hub.HasPlayer && Hub.Health.IsAlive) return true;
            ChangeState(GullState.ClimbToTop);
            return false;
        }

        private Vector3 Prediction()
        {
            Vector3 point = GullTargeting.PredictDropPoint(Hub.PlayerPosition, Hub.PlayerVelocity, config.dropSlotHeight,
                config.telegraphDuration, -Physics.gravity.y);
            var grid = FlightGrid.Instance;
            if (grid != null)
            {
                Bounds bounds = grid.VolumeBounds;
                point.x = Mathf.Clamp(point.x, bounds.min.x + 1, bounds.max.x - 1);
                point.z = Mathf.Clamp(point.z, bounds.min.z + 1, bounds.max.z - 1);
            }
            return point;
        }

        private void ApproachDrop()
        {
            if (!OwnsAttack()) return;
            predictedPoint = Prediction();
            attackTarget = predictedPoint + Vector3.up * config.dropSlotHeight;
            MoveToAttack(attackTarget, false);
            float horizontal = Vector2.Distance(new Vector2(Motor.Position.x, Motor.Position.z), new Vector2(attackTarget.x, attackTarget.z));
            if ((horizontal < .7f && Mathf.Abs(Motor.Position.y - attackTarget.y) < .8f) || stateTime >= 6f)
            {
                if (FlightGrid.Instance == null || !FlightGrid.Instance.IsFlyable(attackTarget)) ChangeState(GullState.ClimbToTop);
                else ChangeState(GullState.Telegraph);
            }
        }

        private void Telegraph()
        {
            if (!OwnsAttack()) return;
            // Aim is committed when the warning begins: a sharp direction change during it must be a real counter.
            MoveToAttack(attackTarget, true);
            Anim.SetWingsSpread(.65f + .35f * Mathf.Sin(stateTime * 24f));
            if (stateTime >= Mathf.Max(0f, config.telegraphDuration)) ChangeState(GullState.Drop);
        }

        private void MoveToAttack(Vector3 target, bool hold)
        {
            float distance = Vector3.Distance(Motor.Position, target);
            // Brake before entering the warning: a fast seek can otherwise overshoot and orbit its aim.
            Motor.SpeedMultiplier = Mathf.Clamp(distance / 5f, .08f, hold ? .6f : 1.3f);
            var grid = FlightGrid.Instance;
            if (grid != null && grid.IsFlyable(target) && PathSmoother.IsClear(Motor.Position, target, grid.SnapLineRadius, grid.ObstacleMask))
            {
                if (hold) Motor.HoldPosition(target); else Motor.FlyTo(target);
            }
            else if (Time.time >= nextRepath)
            {
                if (RequestPath(target) || !LastRequestWasThrottled) nextRepath = Time.time + Mathf.Max(.1f, config.repathInterval);
            }
        }

        private void Drop()
        {
            if (!OwnsAttack()) return;
            if (swooping)
            {
                Motor.SpeedMultiplier = 1.3f;
                var grid = FlightGrid.Instance;
                if (grid != null && PathSmoother.IsClear(Motor.Position, swoopAim, grid.SnapLineRadius, grid.ObstacleMask)) Motor.FlyTo(swoopAim);
                else if (Time.time >= nextRepath && (RequestPath(swoopAim) || !LastRequestWasThrottled)) nextRepath = Time.time + config.repathInterval;
                bool seesPlayer = grid != null && PathSmoother.IsClear(Motor.Position, Hub.PlayerHeadPosition, grid.SnapLineRadius, grid.ObstacleMask);
                if ((seesPlayer && Attack != null && Attack.TryAttack()) || Vector3.Dot(swoopAim - swoopStart, swoopAim - Motor.Position) <= 0 || stateTime >= 4f)
                    ChangeState(GullState.ClimbToTop);
                return;
            }
            if (carriedFod == null) { ChangeState(GullState.ClimbToTop); return; }
            MoveToAttack(attackTarget, true);
            float horizontal = Vector2.Distance(new Vector2(Motor.Position.x, Motor.Position.z), new Vector2(attackTarget.x, attackTarget.z));
            if (horizontal <= .85f && Mathf.Abs(Motor.Position.y - attackTarget.y) <= 1f)
            {
                var released = carriedFod;
                carriedFod = null;
                released.Release();
                FodDropped?.Invoke(released, predictedPoint);
                ChangeState(GullState.ClimbToTop);
            }
            else if (stateTime >= 1.25f) ChangeState(GullState.ClimbToTop); // A badly aligned approach aborts, without teleporting or dropping through cover.
        }

        private void ClimbToTop()
        {
            if (kettle == null || !kettle.Contains(this)) { ChangeState(carriedFod != null ? GullState.JoinKettle : GullState.GatherFod); return; }
            Vector3 top = kettle.GetSlotPosition(this);
            Motor.FlyTo(top);
            if (Vector3.Distance(Motor.Position, top) > 3f) return;
            if (carriedFod != null) ChangeState(GullState.InKettle);
            else { kettle.Leave(this); ChangeState(GullState.GatherFod); }
        }

        internal void OnKettleTokenExpired()
        {
            if (state != GullState.Dead && !previewStateCycle) ChangeState(GullState.ClimbToTop);
        }

        private bool CanReceiveDistress => started && enabled && IsAlive && IsGameplayActive &&
            !previewStateCycle && state != GullState.DistressDive && distressCooldownRemaining <= 0f;

        private void OnDamaged(float damage, float hpAfter)
        {
            // Damaged fires before Died: a lethal hit must not start a group response.
            if (damage <= 0f || hpAfter <= 0f || !CanReceiveDistress) return;
            PlayWarning();
            // Health records the matching shot before this event; do not chase a stale previous shot.
            Vector3 shooter = Health.LastHitShotOrigin;
            BirdRegistry.GetAlive(BirdSpecies.Gull, distressNeighbours);
            float radiusSquared = config.distressRadius * config.distressRadius;
            foreach (var neighbour in distressNeighbours)
            {
                if ((neighbour.transform.position - Motor.Position).sqrMagnitude > radiusSquared) continue;
                var gull = neighbour.GetComponent<GullBrain>();
                if (gull != null) gull.ReceiveDistress(shooter);
            }
        }

        private void ReceiveDistress(Vector3 shooter)
        {
            if (!CanReceiveDistress) return;
            distressCooldownRemaining = Mathf.Max(0f, config.distressCooldown);
            if (kettle != null)
            {
                if (kettle.TokenHolder == this) kettle.ReturnToken(this);
                kettle.Leave(this);
            }
            distressStart = Motor.Position;
            distressAim = shooter + Vector3.up;
            // A state change releases a supply claim, not the piece already being carried.
            ChangeState(GullState.DistressDive);
        }

        private void DistressDive()
        {
            var grid = FlightGrid.Instance;
            bool clearRoute = grid != null && PathSmoother.IsClear(Motor.Position, distressAim, grid.SnapLineRadius, grid.ObstacleMask);
            if (clearRoute) Motor.FlyTo(distressAim);
            else if (Time.time >= nextRepath)
            {
                Motor.Stop();
                if (RequestPath(distressAim) || !LastRequestWasThrottled) nextRepath = Time.time + Mathf.Max(.1f, config.repathInterval);
            }
            bool seesPlayer = Hub != null && Hub.HasPlayer && grid != null &&
                PathSmoother.IsClear(Motor.Position, Hub.PlayerHeadPosition, grid.SnapLineRadius, grid.ObstacleMask);
            if ((seesPlayer && Attack != null && Attack.TryAttack()) ||
                Vector3.Dot(distressAim - distressStart, distressAim - Motor.Position) <= 0f || stateTime >= 4f)
            {
                Motor.Stop();
                ChangeState(GullState.JoinKettle);
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
                    nextRepath = 0f;
                    break;
                case GullState.InKettle:
                    break;
                case GullState.DescendToDrop:
                    nextRepath = 0f;
                    swooping = false;
                    Anim.SetFlying(true);
                    break;
                case GullState.Telegraph:
                    if (!previewStateCycle)
                    {
                        // Snapshot aim/height once. The player remains free to dodge during the warning and fall.
                        predictedPoint = Prediction();
                        attackTarget = predictedPoint + Vector3.up * config.dropSlotHeight;
                        swoopAim = Hub.PlayerPosition + new Vector3(Hub.PlayerVelocity.x, 0, Hub.PlayerVelocity.z) * config.telegraphDuration + Vector3.up;
                        PlayWarning();
                        TelegraphStarted?.Invoke(this);
                    }
                    break;
                case GullState.Drop:
                    if (!previewStateCycle)
                    {
                        swooping = carriedFod == null;
                        swoopStart = Motor.Position;
                        Anim.SetDiving(swooping);
                    }
                    break;
                case GullState.ClimbToTop:
                    Motor.SpeedMultiplier = 1f;
                    Anim.SetDiving(false);
                    Anim.SetWingsSpread(1f);
                    if (!previewStateCycle && kettle != null && kettle.TokenHolder == this) kettle.ReturnToken(this);
                    break;
                case GullState.DistressDive:
                    if (!previewStateCycle)
                    {
                        nextRepath = 0f;
                        swooping = false;
                        Motor.Stop();
                        Motor.SpeedMultiplier = config.distressDiveSpeedMultiplier;
                        Anim.SetDiving(true);
                    }
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
                    Anim.SetDiving(false);
                    Anim.SetWingsSpread(1f);
                    break;
                case GullState.Telegraph:
                    Anim.SetWingsSpread(1f);
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
            if (Health != null) Health.Damaged -= OnDamaged;
            if (warningSource != null) warningSource.Stop();
            Anim.SetDiving(false);
            Anim.SetWingsSpread(1f);
            ReleaseSupply();
            if (kettle != null) kettle.Leave(this);
            if (carriedFod != null) { carriedFod.Despawn(); carriedFod = null; }
            if (Motor != null) { Motor.SpeedMultiplier = 1f; Motor.Stop(); }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Health != null) Health.Damaged += OnDamaged;
            if (started && state != GullState.Dead && !previewStateCycle)
            { ChangeState(GullState.GatherFod); stateTime = 0f; }
        }

        private void PlayWarning()
        {
            if (warningSource == null)
            {
                warningSource = gameObject.AddComponent<AudioSource>();
                warningSource.playOnAwake = false;
                warningSource.spatialBlend = .7f;
                warningSource.minDistance = 8f;
                warningSource.maxDistance = 60f;
                warningSource.volume = .55f;
            }
            if (callClip == null && fallbackCall == null)
            {
                const int rate = 16000, count = 3200;
                var samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)rate;
                    samples[i] = Mathf.Sin(2f * Mathf.PI * (850f * t + 1000f * t * t)) * Mathf.Sin(Mathf.PI * i / count) * .3f;
                }
                fallbackCall = AudioClip.Create("GullWarning_Placeholder", count, 1, rate, false);
                fallbackCall.SetData(samples, 0);
            }
            warningSource.PlayOneShot(callClip != null ? callClip : fallbackCall);
        }

        private void OnDestroy() { if (fallbackCall != null) Destroy(fallbackCall); }
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || (state != GullState.DescendToDrop && state != GullState.Telegraph && state != GullState.Drop)) return;
            Gizmos.color = state == GullState.Telegraph ? Color.yellow : Color.red;
            Gizmos.DrawLine(transform.position, predictedPoint);
            float radius = config != null ? config.fodSplashRadius : 1.5f;
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI / 16, b = (i + 1) * Mathf.PI / 16;
                Gizmos.DrawLine(predictedPoint + new Vector3(Mathf.Cos(a) * radius, .05f, Mathf.Sin(a) * radius),
                    predictedPoint + new Vector3(Mathf.Cos(b) * radius, .05f, Mathf.Sin(b) * radius));
            }
        }
    }
}
