using RunwayPanic.AI;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Chooses where around the player a crow should come from. Pure scoring, no scene access except through
    /// its parameters, so it can be explained (and tested) on its own.
    /// <para>For each candidate point P on a ring around the player:</para>
    /// <code>
    /// score = − danger(P)                         avoid remembered kill zones
    ///         − 0.5 · danger(midpoint crow→P)     avoid crossing them on the way
    ///         + 0.3 · angle(P, last approach)/180 prefer a NEW direction each attack
    ///         − 0.05 · |crow − P| / (2·radius)    tie-breaker: the crow's own side
    /// </code>
    /// <para>Danger reaches 5 per cell, so it dominates; the novelty and distance terms only decide between
    /// equally safe points.</para>
    /// </summary>
    public static class ApproachPointSelector
    {
        /// <summary>One scored candidate (kept for the debug gizmo).</summary>
        public struct Candidate
        {
            /// <summary>World position of the candidate.</summary>
            public Vector3 Point;
            /// <summary>Its score (higher is better). Only meaningful when <see cref="Valid"/>.</summary>
            public float Score;
            /// <summary>False when the point is not flyable and was discarded.</summary>
            public bool Valid;
        }

        private const float MidpointWeight = 0.5f;
        private const float NoveltyWeight = 0.3f;
        private const float DistanceWeight = 0.05f;

        /// <summary>
        /// Scores <paramref name="samples"/> points on a ring of <paramref name="radius"/> around the player at
        /// <paramref name="height"/> above the player's feet and returns the best flyable one.
        /// </summary>
        /// <param name="lastApproachDirection">Flat direction player→point of the previous approach, or zero.</param>
        /// <param name="candidatesOut">Receives every candidate (size ≥ samples), or null.</param>
        /// <returns>False when no candidate was flyable.</returns>
        public static bool Select(Vector3 playerPosition, Vector3 crowPosition, CrowDangerMap danger, IPathProvider paths,
            int samples, float radius, float height, Vector3 lastApproachDirection,
            Candidate[] candidatesOut, out Vector3 bestPoint, out Vector3 bestDirection)
        {
            bestPoint = default;
            bestDirection = default;
            float bestScore = float.NegativeInfinity;
            bool hasLast = lastApproachDirection.sqrMagnitude > 0.0001f;

            // Start the ring on the crow's side so equal scores still give a sensible, stable choice.
            Vector3 toCrow = crowPosition - playerPosition;
            float startAngle = Mathf.Atan2(toCrow.z, toCrow.x);

            for (int i = 0; i < samples; i++)
            {
                float angle = startAngle + i * (Mathf.PI * 2f / samples);
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 point = playerPosition + direction * radius + Vector3.up * height;

                bool valid = paths == null || paths.IsFlyable(point);
                float score = float.NegativeInfinity;
                if (valid)
                {
                    score = 0f;
                    if (danger != null)
                    {
                        score -= danger.GetDanger(point);
                        score -= MidpointWeight * danger.GetDanger((crowPosition + point) * 0.5f);
                    }

                    if (hasLast)
                        score += NoveltyWeight * Vector3.Angle(direction, lastApproachDirection) / 180f;

                    score -= DistanceWeight * Vector3.Distance(crowPosition, point) / (2f * radius);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPoint = point;
                        bestDirection = direction;
                    }
                }

                if (candidatesOut != null && i < candidatesOut.Length)
                    candidatesOut[i] = new Candidate { Point = point, Score = score, Valid = valid };
            }

            return bestScore > float.NegativeInfinity;
        }
    }
}
