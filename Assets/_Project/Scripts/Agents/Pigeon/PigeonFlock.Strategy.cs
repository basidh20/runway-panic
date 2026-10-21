using System.Collections.Generic;
using RunwayPanic.AI;
using RunwayPanic.Core;
using Unity.Profiling;
using UnityEngine;

namespace RunwayPanic.Agents.Pigeon
{
    public sealed partial class PigeonFlock
    {
        private static readonly ProfilerMarker StrategyMarker = new ProfilerMarker("PigeonFlock.Strategy");
        private readonly HashSet<PigeonBrain> divers = new HashSet<PigeonBrain>();
        private readonly Dictionary<PigeonBrain, BirdPoint> roostClaims = new Dictionary<PigeonBrain, BirdPoint>();
        private readonly List<BirdPoint> roosts = new List<BirdPoint>();
        private readonly List<Vector3> candidatePath = new List<Vector3>(64);
        private readonly ICostModifier[] fearCosts = { new FearCostModifier(BirdSpecies.Pigeon) };
        private PlayerEventHub eventHub;
        private Vector3 rally, threat, lastAttackDirection;
        private float alarm, nextDive, nextPanic, nextRallyRefresh;
        private int candidateCursor, attackCursor;
        private bool choosing;
        private float bestScore;
        private Vector3 bestCandidate;

        public Vector3 RallyPoint => rally;
        public Vector3 ThreatPosition => threat;
        public float Alarm => alarm;
        public int ActiveDivers => divers.Count;

        private void BindThreatEvents()
        {
            PlayerEventHub hub = PlayerEventHub.Instance;
            if (hub == eventHub) return;
            UnbindThreatEvents();
            eventHub = hub;
            if (eventHub != null) eventHub.ShotFired += OnShot;
        }

        private void UnbindThreatEvents()
        {
            if (eventHub != null) eventHub.ShotFired -= OnShot;
            eventHub = null;
        }

        private void OnShot(Vector3 origin, Vector3 end, IDamageable target)
        {
            if (config == null || !GameplayActive) return;
            for (int i = 0; i < members.Count; i++)
                if (PigeonNavigation.SegmentDistance(members[i].Position, origin, end) < config.threatRadius)
                {
                    ReportThreat(origin, config.shotAlarm);
                    break; // One gunshot is one stimulus, irrespective of flock size.
                }
        }

        private bool GameplayActive => boundGame == null || boundGame.CurrentState == GameState.Playing;

        public void ReportThreat(Vector3 source, float amount)
        {
            if (!GameplayActive || config == null) return;
            threat = source;
            alarm = Mathf.Clamp01(alarm + Mathf.Max(0f, amount));
        }

        partial void OnMemberJoined(PigeonBrain bird)
        {
            if (members.Count == 1) rally = bird.Position;
        }

        partial void ReleaseClaims(PigeonBrain bird)
        {
            divers.Remove(bird);
            roostClaims.Remove(bird);
        }

        public void ReleaseDive(PigeonBrain bird) => divers.Remove(bird);
        public void ReleaseRoost(PigeonBrain bird) => roostClaims.Remove(bird);

        partial void ResetForNewWave()
        {
            divers.Clear();
            roostClaims.Clear();
            alarm = 0f;
            choosing = false;
            nextDive = nextPanic = nextRallyRefresh = Time.time;
            lastAttackDirection = Vector3.zero;
            for (int i = 0; i < members.Count; i++) members[i].ResetTactics();
        }

        partial void EnterMode(PigeonFlockMode entered)
        {
            if (entered == PigeonFlockMode.Gathering || entered == PigeonFlockMode.Regrouping)
                BeginRallySearch();
            if (entered == PigeonFlockMode.Scattering)
            {
                PigeonStats.Panics++;
                divers.Clear();
                nextPanic = Time.time + config.scatterSeconds + config.panicCooldown;
                for (int i = 0; i < members.Count; i++) members[i].ChangeState(PigeonState.Panic);
            }
        }

        partial void TickMode(PigeonFlockMode current)
        {
            using (StrategyMarker.Auto()) TickStrategy(current);
        }

        private void TickStrategy(PigeonFlockMode current)
        {
            if (config == null || members.Count == 0) return;
            alarm = Mathf.Max(0f, alarm - config.alarmDecay * Time.deltaTime);
            if (eventHub == null || !eventHub.HasPlayer || !eventHub.Health.IsAlive)
            {
                if (mode != PigeonFlockMode.Roosting)
                {
                    divers.Clear();
                    ChangeMode(PigeonFlockMode.Roosting);
                    for (int i = 0; i < members.Count; i++) members[i].ChangeState(PigeonState.FlyToRoost);
                }
                return;
            }
            if (current == PigeonFlockMode.Roosting)
            {
                roostClaims.Clear();
                ChangeMode(PigeonFlockMode.Gathering);
                for (int i = 0; i < members.Count; i++) members[i].ChangeState(PigeonState.Flocking);
                return;
            }
            if (current != PigeonFlockMode.Scattering && Time.time >= nextPanic && alarm >= config.panicThreshold)
            {
                ChangeMode(PigeonFlockMode.Scattering);
                return;
            }
            if (current == PigeonFlockMode.Scattering)
            {
                if (modeTime >= config.scatterSeconds)
                {
                    alarm *= 0.3f;
                    ChangeMode(PigeonFlockMode.Regrouping);
                    for (int i = 0; i < members.Count; i++) members[i].ChangeState(PigeonState.Flocking);
                }
                return;
            }
            if (choosing) ContinueRallySearch();
            if (current == PigeonFlockMode.Gathering || current == PigeonFlockMode.Regrouping)
            {
                int ready = 0;
                for (int i = 0; i < members.Count; i++)
                    if (Vector3.Distance(members[i].Position, rally) <= config.formationRadius + 3f) ready++;
                if (!choosing && (ready >= Mathf.CeilToInt(members.Count * config.readyFraction) || modeTime >= config.gatherTimeout))
                    ChangeMode(PigeonFlockMode.Mobbing);
            }
            else if (current == PigeonFlockMode.Mobbing)
            {
                if (modeTime >= config.mobSeconds || Vector3.Distance(rally, eventHub.PlayerPosition) > config.rallyRadius * 2f)
                {
                    ChangeMode(PigeonFlockMode.Regrouping);
                    return;
                }
                DispatchDive();
            }
            if (!choosing && Time.time >= nextRallyRefresh && current != PigeonFlockMode.Mobbing)
                BeginRallySearch();
        }

        private void DispatchDive()
        {
            if (Time.time < nextDive || divers.Count >= config.maxDivers || eventHub.IsShieldActive) return;
            // Round robin prevents the same nearest bird monopolising attacks.
            for (int i = 0; i < members.Count; i++)
            {
                int index = (attackCursor + i) % members.Count;
                PigeonBrain bird = members[index];
                if (!bird.CanDive || !PigeonNavigation.Clear(bird.Position, eventHub.PlayerHeadPosition)) continue;
                divers.Add(bird);
                attackCursor = (index + 1) % members.Count;
                nextDive = Time.time + config.diveSpacing;
                lastAttackDirection = (bird.Position - eventHub.PlayerPosition).normalized;
                bird.ChangeState(PigeonState.Dive);
                return;
            }
        }

        private void BeginRallySearch()
        {
            choosing = true;
            candidateCursor = 0;
            bestScore = float.NegativeInfinity;
            nextRallyRefresh = Time.time + config.gatherTimeout;
        }

        private void ContinueRallySearch()
        {
            if (eventHub == null || !eventHub.HasPlayer) return;
            // At most one candidate search per frame; every search consumes the shared global budget.
            if (!PathService.TryConsumeBudget()) return;
            float angle = candidateCursor * Mathf.PI * 0.25f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 candidate = PigeonNavigation.Clamp(eventHub.PlayerPosition + direction * config.rallyRadius + Vector3.up * config.flightHeight);
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < members.Count; i++) centre += members[i].Position;
            centre /= members.Count;
            if (PigeonNavigation.Free(candidate) && PathService.Current.TryFindPath(centre, candidate, fearCosts, candidatePath))
            {
                float length = 0f;
                for (int i = 1; i < candidatePath.Count; i++) length += Vector3.Distance(candidatePath[i - 1], candidatePath[i]);
                // Normalised terms: favour a new attack side and shorter, less frightening routes.
                float novelty = (1f - Vector3.Dot(direction, lastAttackDirection)) * 0.5f;
                float safety = Mathf.Clamp01(Vector3.Distance(candidate, threat) / config.threatRadius);
                float fear = FearRegistry.GetRepulsion(candidate, BirdSpecies.Pigeon).magnitude;
                float score = 2f * novelty + safety - length / (config.rallyRadius * 4f) - fear;
                if (score > bestScore) { bestScore = score; bestCandidate = candidate; }
            }
            if (++candidateCursor < 8) return;
            choosing = false;
            if (!float.IsNegativeInfinity(bestScore)) { rally = bestCandidate; PigeonStats.RallySelections++; }
            // No valid candidate: retain the previous goal and retry later, never force a blocked straight flight.
        }

        public Vector3 FormationGoal(PigeonBrain bird)
        {
            int index = Mathf.Max(0, IndexOf(bird));
            float angle = index * 2.399963f + Time.time * 0.25f;
            return PigeonNavigation.Clamp(rally + new Vector3(Mathf.Cos(angle), 0.2f * Mathf.Sin(index), Mathf.Sin(angle)) * config.formationRadius);
        }

        public Vector3 Steering(PigeonBrain bird)
        {
            Vector3 separation = Vector3.zero, centre = Vector3.zero, velocity = Vector3.zero;
            int count = 0;
            int birdIndex = IndexOf(bird);
            for (int i = 0; i < members.Count; i++)
            {
                PigeonBrain other = members[i];
                if (other == bird) continue;
                Vector3 away = bird.Position - other.Position;
                float distance = away.magnitude;
                if (distance > config.neighbourRadius) continue;
                if (distance < config.separationRadius)
                    separation += distance > 0.01f ? away / Mathf.Max(0.1f, distance * distance)
                        : (birdIndex > i ? Vector3.right : Vector3.left);
                centre += other.Position;
                velocity += other.Velocity;
                count++;
            }
            Vector3 result = separation * config.separationWeight;
            if (count > 0)
                result += Vector3.ClampMagnitude(centre / count - bird.Position, 1f) * config.cohesionWeight
                    + Vector3.ClampMagnitude(velocity / count - bird.Velocity, 1f) * config.alignmentWeight;
            result += FearRegistry.GetRepulsion(bird.Position, BirdSpecies.Pigeon);
            return Vector3.ClampMagnitude(result, config.steeringLimit);
        }

        public bool TryClaimRoost(PigeonBrain bird, out Vector3 point)
        {
            if (roostClaims.TryGetValue(bird, out BirdPoint existing) && existing != null)
            { point = existing.Position + Vector3.up * 0.35f; return true; }
            BirdPoint.GetAll(BirdPointType.Roost, roosts);
            BirdPoint best = null;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < roosts.Count; i++)
            {
                BirdPoint roost = roosts[i];
                if (roostClaims.ContainsValue(roost)) continue; // One slot per marker, including the small wing roost.
                float d = (roost.Position - bird.Position).sqrMagnitude;
                if (d < distance) { distance = d; best = roost; }
            }
            if (best != null) { roostClaims[bird] = best; point = best.Position + Vector3.up * 0.35f; return true; }
            point = bird.Position;
            return false;
        }
    }
}
