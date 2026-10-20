using RunwayPanic.AI;
using UnityEngine;

namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>Species-local safety queries. Shared navigation and motor implementations are unchanged.</summary>
    public static class PigeonNavigation
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        public static Vector3 Clamp(Vector3 point)
        {
            FlightGrid grid = FlightGrid.Instance;
            if (grid == null) return point;
            Bounds bounds = grid.VolumeBounds;
            float margin = Mathf.Max(1f, grid.Clearance + grid.FlyableRadius);
            return new Vector3(Mathf.Clamp(point.x, bounds.min.x + margin, bounds.max.x - margin),
                Mathf.Clamp(point.y, bounds.min.y + margin, bounds.max.y - margin),
                Mathf.Clamp(point.z, bounds.min.z + margin, bounds.max.z - margin));
        }

        public static bool Free(Vector3 point)
        {
            FlightGrid grid = FlightGrid.Instance;
            return grid != null ? grid.IsFlyable(point) : FlightGrid.IsSpaceFree(point, 0.4f, ~0);
        }

        public static bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.001f) return Free(to);
            FlightGrid grid = FlightGrid.Instance;
            int count = Physics.SphereCastNonAlloc(from, 0.35f, delta.normalized, Hits, delta.magnitude,
                grid != null ? grid.ObstacleMask : (LayerMask)~0, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) return false;
            for (int i = 0; i < count; i++)
                if (!FlightGrid.IsAgentCollider(Hits[i].collider)) return false;
            return Free(to);
        }

        public static float SegmentDistance(Vector3 point, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float t = delta.sqrMagnitude > 0.001f ? Mathf.Clamp01(Vector3.Dot(point - from, delta) / delta.sqrMagnitude) : 0f;
            return Vector3.Distance(point, from + delta * t);
        }
    }
}
