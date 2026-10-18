using RunwayPanic.AI;
using UnityEngine;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// One perch's utility score with every term kept, so the debug labels (and the viva) can show why a perch won.
    /// Terms are 0..1 before weighting; <see cref="Total"/> is the weighted sum.
    /// </summary>
    public readonly struct PerchScore
    {
        /// <summary>The scored perch.</summary>
        public readonly BirdPoint Perch;
        /// <summary>Height above the player, 0..1 (1 at <c>maxHeightBonus</c> or higher).</summary>
        public readonly float Height;
        /// <summary>1 when the perch can see the player's head, else 0.</summary>
        public readonly float LineOfSight;
        /// <summary>Closeness to the ideal distance, 0..1 (bell curve).</summary>
        public readonly float Distance;
        /// <summary>1 when the perch is between the player and the sun, 0 when behind or beside the player.</summary>
        public readonly float Sun;
        /// <summary>Recently-shot value from <see cref="PerchMemory"/>, 0..1.</summary>
        public readonly float RecentlyShot;
        /// <summary>Weighted total used to pick the perch.</summary>
        public readonly float Total;
        /// <summary>Straight-line distance from the perch to the player's head, in metres.</summary>
        public readonly float Metres;

        /// <summary>Creates a score.</summary>
        public PerchScore(BirdPoint perch, float height, float lineOfSight, float distance, float sun, float recentlyShot, float total, float metres)
        {
            Perch = perch;
            Height = height;
            LineOfSight = lineOfSight;
            Distance = distance;
            Sun = sun;
            RecentlyShot = recentlyShot;
            Total = total;
            Metres = metres;
        }
    }

    /// <summary>
    /// Utility AI for the hawk's perch choice: scores a perch from five terms (height, line of sight, distance, sun,
    /// recently shot) and their weights in <see cref="HawkStrategyConfig"/>. Pure maths plus one raycast per perch;
    /// no state, no MonoBehaviour.
    /// <para>Hunt: <c>wHeight·height + wLineOfSight·los + wDistance·distance + wSun·sun − wRecentlyShot·shot</c>.
    /// Withdraw: the line-of-sight term is subtracted instead (hide from the player) and the sun term is dropped.</para>
    /// </summary>
    public static class PerchSelector
    {
        private const int HitBufferSize = 16;
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[HitBufferSize];

        /// <summary>
        /// Scores <paramref name="perch"/> against the player. <paramref name="sunForward"/> is the direction the
        /// sunlight travels (the light's forward). <paramref name="losHeight"/> lifts the sight line's start above the
        /// perch surface (the hawk's eye height).
        /// </summary>
        public static PerchScore Score(BirdPoint perch, Vector3 playerFeet, Vector3 playerHead, Vector3 sunForward,
            PerchMemory memory, HawkStrategyConfig config, bool withdrawMode, float losHeight, LayerMask obstacleMask)
        {
            Vector3 perchPosition = perch.Position;

            float height = Mathf.Clamp01((perchPosition.y - playerFeet.y) / config.MaxHeightBonus);

            Vector3 eye = perchPosition + Vector3.up * losHeight;
            float los = HasLineOfSight(eye, playerHead, obstacleMask) ? 1f : 0f;

            float metres = Vector3.Distance(perchPosition, playerHead);
            float offset = (metres - config.IdealDistance) / config.DistanceWidth;
            float distance = Mathf.Exp(-offset * offset);

            Vector3 fromPlayer = perchPosition - playerFeet;
            float sun = fromPlayer.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(fromPlayer.normalized, -sunForward.normalized)) : 0f;

            float shot = memory != null ? memory.Get(perch) : 0f;

            float total = withdrawMode
                ? config.WHeight * height - config.WLineOfSight * los + config.WDistance * distance - config.WRecentlyShot * shot
                : config.WHeight * height + config.WLineOfSight * los + config.WDistance * distance + config.WSun * sun - config.WRecentlyShot * shot;

            return new PerchScore(perch, height, los, distance, sun, shot, total, metres);
        }

        /// <summary>
        /// True when nothing but birds, the player or other things with HP lies between <paramref name="from"/> and
        /// <paramref name="to"/> (so the player's own collider and other birds never block the view).
        /// </summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to, LayerMask obstacleMask)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude;
            if (length < 0.0001f)
                return true;

            int count = Physics.RaycastNonAlloc(from, direction / length, HitBuffer, length, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!FlightGrid.IsAgentCollider(HitBuffer[i].collider))
                    return false;
            // A full buffer may hide a level collider behind agent colliders: treat it as blocked.
            return count < HitBufferSize;
        }
    }
}
