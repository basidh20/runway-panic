using System.Collections.Generic;
using RunwayPanic.AI;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Patrol: wander between random flyable points 10–20 m apart at 4–8 m above the ground, flying there along
    /// A* paths. Also holds the fear-field helpers every crow movement state uses, so the hawk can herd crows.
    /// </summary>
    public sealed partial class CrowBrain
    {
        private const int PatrolPointAttempts = 8;
        private const float GroundProbeHeight = 50f;

        private readonly List<ICostModifier> patrolCosts = new List<ICostModifier>(1);
        private FearCostModifier fearCost;
        private Vector3 patrolTarget;
        private bool hasPatrolTarget;
        private bool patrolPathRequested;
        private float nextPatrolRequestTime;

        /// <summary>Fear cost for crows: lets the hawk's fear field push crow paths away from it.</summary>
        private FearCostModifier FearCost => fearCost ??= new FearCostModifier(BirdSpecies.Crow);

        partial void EnterPatrol()
        {
            Motor.SpeedMultiplier = 1f;
            hasPatrolTarget = false;
            patrolPathRequested = false;
        }

        partial void TickPatrol()
        {
            AddFearSteering();

            // Arrived (or the path ended): pick the next waypoint.
            if (hasPatrolTarget && patrolPathRequested && (Motor.ReachedDestination || !Motor.HasPath))
                hasPatrolTarget = false;

            if (!hasPatrolTarget)
            {
                if (!TryPickPatrolPoint(out patrolTarget))
                    return;
                hasPatrolTarget = true;
                patrolPathRequested = false;
            }

            if (patrolPathRequested || Time.time < nextPatrolRequestTime)
                return;

            if (patrolCosts.Count == 0)
                patrolCosts.Add(FearCost);

            if (RequestPath(patrolTarget, patrolCosts))
            {
                patrolPathRequested = true;
            }
            else if (!LastRequestWasThrottled)
            {
                // No route to that point: forget it and try another one shortly.
                hasPatrolTarget = false;
                nextPatrolRequestTime = Time.time + config.repathInterval;
            }
        }

        /// <summary>Adds the hawk's fear repulsion to this frame's movement (no-op when there is no fear source).</summary>
        private void AddFearSteering()
        {
            Vector3 repulsion = FearRegistry.GetRepulsion(Motor.Position, BirdSpecies.Crow);
            if (repulsion.sqrMagnitude > 0.0001f)
                Motor.AddSteering(repulsion);
        }

        /// <summary>
        /// Samples a random point 10–20 m away at patrol height and keeps the first one a bird can fly to.
        /// </summary>
        private bool TryPickPatrolPoint(out Vector3 point)
        {
            Vector3 origin = Motor.Position;
            FlightGrid grid = FlightGrid.Instance;

            for (int i = 0; i < PatrolPointAttempts; i++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                float distance = Random.Range(config.patrolMinDistance, config.patrolMaxDistance);
                Vector3 candidate = origin + new Vector3(direction.x, 0f, direction.y) * distance;
                candidate.y = GroundHeightAt(candidate) + Random.Range(config.patrolMinHeight, config.patrolMaxHeight);

                if (grid != null)
                {
                    // Stay one cell inside the flight volume so the point is always on the grid.
                    Bounds volume = grid.VolumeBounds;
                    volume.Expand(-grid.CellSize * 2f);
                    candidate = volume.ClosestPoint(candidate);
                }

                if (PathService.Current.IsFlyable(candidate))
                {
                    point = candidate;
                    return true;
                }
            }

            point = default;
            return false;
        }

        /// <summary>Height of the solid surface under <paramref name="position"/> (0 if nothing is hit).</summary>
        private static float GroundHeightAt(Vector3 position)
        {
            Vector3 from = new Vector3(position.x, GroundProbeHeight, position.z);
            return Physics.Raycast(from, Vector3.down, out RaycastHit hit, GroundProbeHeight * 2f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : 0f;
        }
    }
}
