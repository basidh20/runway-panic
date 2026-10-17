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

        /// <summary>
        /// Cost-aware version of <see cref="Smooth(List{Vector3}, float, LayerMask)"/>: a shortcut must also be
        /// free of walls AND carry no more extra cost (danger, fear) than the part of the path it replaces.
        /// Without this, string-pulling in open air straightens every cost-driven detour back through the area
        /// A* chose to avoid. With no modifiers it behaves exactly like the plain version.
        /// </summary>
        /// <param name="sampleSpacing">Distance between cost samples along a segment (use the grid cell size).</param>
        public static void Smooth(List<Vector3> path, float radius, LayerMask mask,
            IReadOnlyList<ICostModifier> costModifiers, float sampleSpacing)
        {
            if (costModifiers == null || costModifiers.Count == 0)
            {
                Smooth(path, radius, mask);
                return;
            }

            if (path == null || path.Count < 3)
                return;

            // Same compaction as the plain version. Points after anchorIndex are still the original ones,
            // so the replaced part of the path can be costed before it is overwritten.
            Vector3 anchor = path[0];
            int anchorIndex = 0;
            int write = 1;
            for (int i = 2; i < path.Count; i++)
            {
                if (IsClear(anchor, path[i], radius, mask) &&
                    ExtraCost(anchor, path[i], costModifiers, sampleSpacing) <=
                    PolylineExtraCost(path, anchorIndex, i, costModifiers, sampleSpacing) + CostTolerance)
                    continue;

                anchor = path[i - 1];
                anchorIndex = i - 1;
                path[write++] = anchor;
            }

            path[write++] = path[path.Count - 1];
            path.RemoveRange(write, path.Count - write);
        }

        private const float CostTolerance = 0.01f;

        // Extra cost of flying the straight segment a→b: Σ cost(sample) × step length.
        private static float ExtraCost(Vector3 a, Vector3 b, IReadOnlyList<ICostModifier> modifiers, float spacing)
        {
            float length = Vector3.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.1f, spacing)));
            float stepLength = length / steps;
            float total = 0f;
            for (int s = 1; s <= steps; s++)
            {
                Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                for (int m = 0; m < modifiers.Count; m++)
                    total += modifiers[m].GetExtraCost(p) * stepLength;
            }

            return total;
        }

        // Extra cost of following the original path from index 'from' to index 'to'.
        private static float PolylineExtraCost(List<Vector3> path, int from, int to, IReadOnlyList<ICostModifier> modifiers, float spacing)
        {
            float total = 0f;
            for (int k = from; k < to; k++)
                total += ExtraCost(path[k], path[k + 1], modifiers, spacing);
            return total;
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
