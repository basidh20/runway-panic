using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Turns a list of path points (e.g. the cell centres of an A* grid path) into a smooth curve that a bird can fly
    /// along at a constant speed:
    /// 1. drops points that lie on a straight line between their neighbours (a grid path has one point per cell, but
    ///    only the corners change the route),
    /// 2. fits a centripetal Catmull-Rom spline through the remaining corners (it passes through every corner and,
    ///    unlike the uniform version, never overshoots or loops when corners are unevenly spaced),
    /// 3. samples the curve into a polyline and stores the distance travelled at every sample (an arc-length table),
    ///    so "where is the bird after s metres?" is a binary search instead of solving the spline.
    /// Built once per new path, never per frame. Every buffer is reused, so a rebuild does not allocate once the
    /// buffers are big enough.
    /// </summary>
    public sealed class CatmullRomPath
    {
        // Points closer than this are the same point: a zero-length span would divide by zero in the spline.
        private const float MinPointSpacing = 0.01f;
        // Floor for the spline's knot spacing, again to avoid dividing by zero.
        private const float MinKnot = 1e-4f;

        private readonly List<Vector3> corners = new List<Vector3>(32);
        private Vector3[] samples = new Vector3[256];
        private float[] distances = new float[256];

        /// <summary>Points the curve passes through after simplification (start, optional lead-in, corners, end).</summary>
        public IReadOnlyList<Vector3> Corners => corners;

        /// <summary>Number of points in the sampled polyline.</summary>
        public int SampleCount { get; private set; }

        /// <summary>Length of the curve in metres.</summary>
        public float Length { get; private set; }

        /// <summary>Last point of the curve (the final waypoint).</summary>
        public Vector3 EndPoint => SampleCount > 0 ? samples[SampleCount - 1] : Vector3.zero;

        public Vector3 GetSample(int index) => samples[index];

        public float GetSampleDistance(int index) => distances[index];

        public void Clear()
        {
            corners.Clear();
            SampleCount = 0;
            Length = 0f;
        }

        /// <summary>
        /// Builds the curve from <paramref name="start"/> through <paramref name="points"/> (from index
        /// <paramref name="firstPoint"/> on).
        /// </summary>
        /// <param name="start">Where the curve begins: the bird's current position.</param>
        /// <param name="startDirection">The bird's current flight direction, or zero if it is not moving. The curve
        /// leaves <paramref name="start"/> exactly along it, so a new path never makes the bird snap around.</param>
        /// <param name="leadIn">Length of the straight piece along <paramref name="startDirection"/> before the curve
        /// may turn.</param>
        /// <param name="points">Waypoints in world space.</param>
        /// <param name="firstPoint">Index of the first waypoint to use; earlier ones are already behind the bird.</param>
        /// <param name="collinearToleranceDegrees">A point where the route bends less than this is dropped.</param>
        /// <param name="sampleSpacing">Approximate distance between samples, in metres.</param>
        public void Build(Vector3 start, Vector3 startDirection, float leadIn, IReadOnlyList<Vector3> points,
            int firstPoint, float collinearToleranceDegrees, float sampleSpacing)
        {
            Clear();
            corners.Add(start);

            bool hasDirection = startDirection.sqrMagnitude > 1e-6f;
            Vector3 direction = hasDirection ? startDirection.normalized : Vector3.zero;
            if (hasDirection && leadIn > MinPointSpacing)
                corners.Add(start + direction * leadIn);

            // The start (and the lead-in point) set the starting tangent, so simplification must never remove them.
            int protectedCount = corners.Count;
            float minCos = Mathf.Cos(collinearToleranceDegrees * Mathf.Deg2Rad);

            for (int i = firstPoint; i < points.Count; i++)
                AddCorner(points[i], protectedCount, minCos);

            BuildSamples(hasDirection ? direction : Vector3.zero, leadIn, Mathf.Max(0.01f, sampleSpacing));
        }

        /// <summary>
        /// Point on the curve <paramref name="distance"/> metres from the start (clamped to the curve).
        /// </summary>
        public Vector3 PointAtDistance(float distance)
        {
            if (SampleCount == 0)
                return Vector3.zero;
            if (SampleCount == 1)
                return samples[0];

            distance = Mathf.Clamp(distance, 0f, Length);
            int i = Mathf.Min(IndexAtDistance(distance), SampleCount - 2);
            float span = distances[i + 1] - distances[i];
            float t = span > 1e-6f ? (distance - distances[i]) / span : 0f;
            return Vector3.LerpUnclamped(samples[i], samples[i + 1], t);
        }

        /// <summary>
        /// Distance along the curve of the point closest to <paramref name="point"/>, searching only between
        /// <paramref name="fromDistance"/> and <paramref name="toDistance"/>. A short window is cheap and stops the
        /// answer from jumping to a later part of the path that happens to pass close by.
        /// </summary>
        public float ClosestDistance(Vector3 point, float fromDistance, float toDistance)
        {
            if (SampleCount < 2)
                return 0f;

            int first = IndexAtDistance(Mathf.Max(0f, fromDistance));
            int last = Mathf.Min(IndexAtDistance(Mathf.Min(toDistance, Length)) + 1, SampleCount - 1);
            last = Mathf.Max(last, Mathf.Min(first + 1, SampleCount - 1));

            float bestSqr = float.MaxValue;
            float best = distances[first];
            for (int i = first; i < last; i++)
            {
                Vector3 a = samples[i];
                Vector3 ab = samples[i + 1] - a;
                float lengthSqr = ab.sqrMagnitude;
                float t = lengthSqr > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSqr) : 0f;
                float sqr = (point - (a + ab * t)).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = Mathf.Lerp(distances[i], distances[i + 1], t);
                }
            }

            return best;
        }

        /// <summary>Index of the last sample at or before <paramref name="distance"/> (binary search).</summary>
        public int IndexAtDistance(float distance)
        {
            int low = 0;
            int high = SampleCount - 1;
            while (low < high)
            {
                int mid = (low + high + 1) >> 1;
                if (distances[mid] <= distance)
                    low = mid;
                else
                    high = mid - 1;
            }

            return low;
        }

        /// <summary>
        /// Adds a waypoint, replacing the previous corner when that corner lies on the straight line from the corner
        /// before it to the new point.
        /// </summary>
        private void AddCorner(Vector3 point, int protectedCount, float minCos)
        {
            Vector3 last = corners[corners.Count - 1];
            if ((point - last).sqrMagnitude < MinPointSpacing * MinPointSpacing)
                return; // duplicate

            if (corners.Count > protectedCount)
            {
                Vector3 before = corners[corners.Count - 2];
                Vector3 incoming = (last - before).normalized;
                Vector3 outgoing = (point - last).normalized;
                if (Vector3.Dot(incoming, outgoing) >= minCos)
                {
                    corners[corners.Count - 1] = point; // "last" was not a corner
                    return;
                }
            }

            corners.Add(point);
        }

        private void BuildSamples(Vector3 startDirection, float leadIn, float sampleSpacing)
        {
            int segmentCount = corners.Count - 1;
            if (segmentCount < 1)
            {
                EnsureCapacity(1);
                samples[0] = corners[0];
                distances[0] = 0f;
                SampleCount = 1;
                return;
            }

            // Catmull-Rom needs one extra point before the first corner and after the last. At the start it sits
            // behind the bird along its flight direction, so the curve leaves the bird exactly along that direction.
            // Otherwise the end points are mirrored, which gives a natural, straight-ish start and finish.
            Vector3 first = corners[0];
            Vector3 beforeStart = startDirection != Vector3.zero
                ? first - startDirection * Mathf.Max(leadIn, MinPointSpacing)
                : 2f * first - corners[1];
            Vector3 afterEnd = 2f * corners[segmentCount] - corners[segmentCount - 1];

            int total = 1;
            for (int i = 0; i < segmentCount; i++)
                total += StepsFor(corners[i], corners[i + 1], sampleSpacing);
            EnsureCapacity(total);

            int count = 0;
            float length = 0f;
            samples[count] = first;
            distances[count] = 0f;
            count++;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 p0 = i == 0 ? beforeStart : corners[i - 1];
                Vector3 p1 = corners[i];
                Vector3 p2 = corners[i + 1];
                Vector3 p3 = i + 2 <= segmentCount ? corners[i + 2] : afterEnd;

                int steps = StepsFor(p1, p2, sampleSpacing);
                for (int j = 1; j <= steps; j++)
                {
                    Vector3 point = j == steps ? p2 : Evaluate(p0, p1, p2, p3, (float)j / steps);
                    length += Vector3.Distance(samples[count - 1], point);
                    samples[count] = point;
                    distances[count] = length;
                    count++;
                }
            }

            SampleCount = count;
            Length = length;
        }

        private static int StepsFor(Vector3 a, Vector3 b, float spacing) =>
            Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / spacing));

        /// <summary>
        /// Centripetal Catmull-Rom between <paramref name="p1"/> (u = 0) and <paramref name="p2"/> (u = 1), using the
        /// Barry-Goldman pyramid form. "Centripetal" means the knot spacing is the square root of the distance between
        /// points (alpha = 0.5), which keeps the curve close to the corners with no cusps or self-loops.
        /// </summary>
        private static Vector3 Evaluate(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float t0 = 0f;
            float t1 = t0 + Knot(p0, p1);
            float t2 = t1 + Knot(p1, p2);
            float t3 = t2 + Knot(p2, p3);
            float t = Mathf.Lerp(t1, t2, u);

            Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }

        // |b - a| ^ 0.5, written as the 4th root of the squared distance to avoid a square root and a Pow.
        private static float Knot(Vector3 a, Vector3 b) => Mathf.Max(Mathf.Sqrt(Mathf.Sqrt((b - a).sqrMagnitude)), MinKnot);

        private void EnsureCapacity(int count)
        {
            if (samples.Length >= count)
                return;

            int size = samples.Length;
            while (size < count)
                size *= 2;
            samples = new Vector3[size];
            distances = new float[size];
        }
    }
}
