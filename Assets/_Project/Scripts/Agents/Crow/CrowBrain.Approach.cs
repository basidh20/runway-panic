using System.Collections.Generic;
using RunwayPanic.AI;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Approach: once the player is within detect range, pick the best approach point with
    /// <see cref="ApproachPointSelector"/> and fly there along A* paths that include the danger map as an
    /// extra cost. This is what makes crows route around the player's favourite firing positions.
    /// </summary>
    public sealed partial class CrowBrain
    {
        // Leave the chase when the player gets this much further than detect range (hysteresis, no flicker).
        private const float LoseInterestFactor = 1.5f;
        private const float ArriveDistance = 2f;

        private readonly List<ICostModifier> approachCosts = new List<ICostModifier>(2);
        private ApproachPointSelector.Candidate[] approachCandidates;
        private Vector3 approachDirection;      // flat player→point direction of the current approach
        private Vector3 lastApproachDirection;  // the previous one, for the novelty term
        private float nextApproachRepathTime;

        /// <summary>
        /// Costs every careful crow path uses: the remembered danger plus the hawk's fear field.
        /// Rebuilt when the danger map appears, so crows started before it still use it.
        /// </summary>
        private List<ICostModifier> ApproachCosts
        {
            get
            {
                CrowDangerMap map = CrowDangerMap.Instance;
                if (approachCosts.Count == 0 || (map != null && !approachCosts.Contains(map)))
                {
                    approachCosts.Clear();
                    if (map != null)
                        approachCosts.Add(map);
                    approachCosts.Add(FearCost);
                }

                return approachCosts;
            }
        }

        // The approach point follows the player: same direction and distance from wherever they are now.
        private Vector3 ApproachTarget =>
            Hub.PlayerPosition + approachDirection * config.approachRingRadius + Vector3.up * config.approachHeight;

        partial void UpdateDetection()
        {
            float range = config.detectRange;
            if ((Hub.PlayerPosition - Motor.Position).sqrMagnitude <= range * range)
                ChangeState(CrowState.Approach);
        }

        partial void EnterApproach()
        {
            Motor.SpeedMultiplier = 1f;
            ChooseApproachPoint();
            nextApproachRepathTime = 0f;
        }

        partial void TickApproach()
        {
            AddFearSteering();

            float lose = config.detectRange * LoseInterestFactor;
            if ((Hub.PlayerPosition - Motor.Position).sqrMagnitude > lose * lose)
            {
                ChangeState(CrowState.Patrol);
                return;
            }

            Vector3 target = ApproachTarget;
            if ((target - Motor.Position).sqrMagnitude <= ArriveDistance * ArriveDistance)
            {
                ChangeState(CrowState.Attack);
                return;
            }

            if (Time.time < nextApproachRepathTime)
                return;

            // A throttled request keeps the timer, so it is retried next frame. A real failure (e.g. the player is
            // inside a sealed shelter) waits a full interval before trying again, so it cannot spike the frame.
            if (RequestPath(target, ApproachCosts) || !LastRequestWasThrottled)
                nextApproachRepathTime = Time.time + config.repathInterval;
        }

        /// <summary>Scores the ring around the player and stores the winning direction.</summary>
        private void ChooseApproachPoint()
        {
            if (approachCandidates == null || approachCandidates.Length != config.approachSamples)
                approachCandidates = new ApproachPointSelector.Candidate[config.approachSamples];

            lastApproachDirection = approachDirection;
            bool found = ApproachPointSelector.Select(Hub.PlayerPosition, Motor.Position, CrowDangerMap.Instance,
                PathService.Current, config.approachSamples, config.approachRingRadius, config.approachHeight,
                lastApproachDirection, approachCandidates, out _, out Vector3 direction);

            if (!found)
            {
                // Nothing flyable around the player (e.g. they stand in a narrow gap): come straight from our side.
                direction = Motor.Position - Hub.PlayerPosition;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            }

            approachDirection = direction;
            CrowStats.Approaches++;
        }

        private void OnDrawGizmosSelected()
        {
            if (approachCandidates != null && (state == CrowState.Approach || state == CrowState.Attack))
            {
                // Candidates coloured from red (worst) to green (best); the chosen direction drawn large.
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (ApproachPointSelector.Candidate c in approachCandidates)
                {
                    if (!c.Valid) continue;
                    min = Mathf.Min(min, c.Score);
                    max = Mathf.Max(max, c.Score);
                }

                foreach (ApproachPointSelector.Candidate c in approachCandidates)
                {
                    float t = c.Valid && max > min ? (c.Score - min) / (max - min) : (c.Valid ? 1f : 0f);
                    Gizmos.color = c.Valid ? Color.Lerp(Color.red, Color.green, t) : Color.grey;
                    Gizmos.DrawSphere(c.Point, 0.25f);
                }

                if (Hub != null && Hub.HasPlayer)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawSphere(ApproachTarget, 0.5f);
                    Gizmos.DrawLine(transform.position, ApproachTarget);
                }
            }

            DrawFuneralGizmos();
        }

        partial void DrawFuneralGizmos();
    }
}
