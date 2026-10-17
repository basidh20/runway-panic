using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// Removes the staircase from grid paths by string-pulling: from the current anchor point, keep walking
    /// forward while a sphere can sweep straight to the next point; at the first point it can't reach,
    /// keep the previous one as the new anchor. What remains are only the points where the path must bend.
    /// </summary>
    public static class PathSmoother
    {
        private const int HitBufferSize = 16;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[HitBufferSize];

        /// <summary>
        /// Smooths <paramref name="path"/> in place. The first and last points are always kept.
        /// <paramref name="radius"/> is the sweep radius (use the grid clearance); birds and the player never block.
        /// </summary>
        public static void Smooth(List<Vector3> path, float radius, LayerMask mask)
        {
            if (path == null || path.Count < 3)
                return;

            // Points are compacted toward the front: write never passes the point being read.
            Vector3 anchor = path[0];
            int write = 1;
            for (int i = 2; i < path.Count; i++)
            {
                if (IsClear(anchor, path[i], radius, mask))
                    continue;

                anchor = path[i - 1];
                path[write++] = anchor;
            }

            path[write++] = path[path.Count - 1];
            path.RemoveRange(write, path.Count - write);
        }

        /// <summary>True when a sphere of <paramref name="radius"/> can sweep from <paramref name="from"/> to <paramref name="to"/> without hitting level geometry.</summary>
        public static bool IsClear(Vector3 from, Vector3 to, float radius, LayerMask mask)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
                return true;

            int count = Physics.SphereCastNonAlloc(from, radius, delta / distance, HitBuffer, distance, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!FlightGrid.IsAgentCollider(HitBuffer[i].collider))
                    return false;
            // A full buffer may hide a level collider behind agent colliders: treat it as blocked.
            return count < HitBufferSize;
        }
    }
}
