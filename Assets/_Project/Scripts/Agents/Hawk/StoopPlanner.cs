using RunwayPanic.AI;
using UnityEngine;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// Pure maths for the hawk's stoop (dive): where the dive starts so it comes out of the sun, and where to aim so it
    /// meets a moving player. No state, no MonoBehaviour.
    /// <para>Stoop start: on the sun's side of the player (same compass direction as the sun, so a player tracking the
    /// diving hawk looks toward the sun), <c>distance</c> metres away, at the sun's elevation but never steeper than
    /// <c>maxAngle</c> — the motor cannot dive steeper than its max pitch (BirdMotion_Hawk: 30°), and a start above that
    /// angle makes the hawk overshoot the player. At least <c>minHeight</c> above the player; if that would make the
    /// angle too steep, the start moves further out instead.</para>
    /// </summary>
    public static class StoopPlanner
    {
        /// <summary>
        /// Stoop start point (see the class summary). <paramref name="sunForward"/> = the direction sunlight travels.
        /// <paramref name="yawDegrees"/> turns the compass direction around the player (0 = exactly sun-side), used to
        /// step around buildings that block the dive line.
        /// </summary>
        public static Vector3 GetStoopStart(Vector3 playerPosition, Vector3 sunForward, float distance, float minHeight,
            float maxAngleDegrees, float yawDegrees)
        {
            Vector3 towardSun = -sunForward.normalized;
            Vector3 flat = new Vector3(towardSun.x, 0f, towardSun.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward; // sun straight overhead: any side
            flat = Quaternion.AngleAxis(yawDegrees, Vector3.up) * flat;

            float sunElevation = Mathf.Asin(Mathf.Clamp(towardSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            float angle = Mathf.Clamp(sunElevation, 1f, maxAngleDegrees) * Mathf.Deg2Rad;
            float maxAngle = Mathf.Max(maxAngleDegrees, 1f) * Mathf.Deg2Rad;

            float height = Mathf.Max(distance * Mathf.Sin(angle), minHeight);
            float horizontal = distance * Mathf.Cos(angle);
            if (Mathf.Atan2(height, horizontal) > maxAngle)
                horizontal = height / Mathf.Tan(maxAngle); // too steep for the motor: move out, not up

            return playerPosition + flat * horizontal + Vector3.up * height;
        }

        /// <summary>Moves <paramref name="point"/> inside <paramref name="volume"/> shrunk by <paramref name="margin"/> on every side.</summary>
        public static Vector3 ClampToVolume(Vector3 point, Bounds volume, float margin)
        {
            Vector3 min = volume.min + Vector3.one * margin;
            Vector3 max = volume.max - Vector3.one * margin;
            return new Vector3(Mathf.Clamp(point.x, min.x, max.x), Mathf.Clamp(point.y, min.y, max.y), Mathf.Clamp(point.z, min.z, max.z));
        }

        /// <summary>
        /// The nearest flyable point at or above <paramref name="point"/>, searching up to <paramref name="maxRise"/>
        /// metres in <paramref name="step"/> steps. Returns false (and <paramref name="point"/> unchanged) if none.
        /// </summary>
        public static bool TrySnapUpToFlyable(Vector3 point, IPathProvider paths, float maxRise, float step, out Vector3 result)
        {
            for (float rise = 0f; rise <= maxRise; rise += step)
            {
                Vector3 candidate = point + Vector3.up * rise;
                if (paths.IsFlyable(candidate))
                {
                    result = candidate;
                    return true;
                }
            }

            result = point;
            return false;
        }

        /// <summary>Where to dive so the hawk meets a moving player: the aim point plus the player's velocity × <paramref name="leadTime"/>.</summary>
        public static Vector3 GetLeadPoint(Vector3 aimPoint, Vector3 playerVelocity, float leadTime)
        {
            return aimPoint + playerVelocity * leadTime;
        }

        /// <summary>
        /// Strike-through target: <paramref name="overshoot"/> metres beyond <paramref name="leadPoint"/> along the line
        /// from <paramref name="hawkPosition"/>, and never lower than <paramref name="minHeight"/>. The motor brakes to stop
        /// at a seek target, so aiming past the player keeps the dive fast where it meets them.
        /// </summary>
        public static Vector3 GetStrikeThroughTarget(Vector3 hawkPosition, Vector3 leadPoint, float overshoot, float minHeight)
        {
            Vector3 line = leadPoint - hawkPosition;
            Vector3 target = line.sqrMagnitude > 1e-6f ? leadPoint + line.normalized * overshoot : leadPoint;
            target.y = Mathf.Max(target.y, minHeight);
            return target;
        }

        /// <summary>
        /// Carrot for circling: the point on the horizontal circle (<paramref name="center"/>, <paramref name="radius"/>)
        /// <paramref name="leadDegrees"/> ahead of the hawk's own angle around the centre (counter-clockwise seen from
        /// above). Chasing it every frame makes a turn-limited bird circle smoothly at full speed, and it works from any
        /// start point (the hawk first spirals onto the circle).
        /// </summary>
        public static Vector3 GetOrbitTarget(Vector3 center, float radius, float leadDegrees, Vector3 hawkPosition)
        {
            Vector3 offset = hawkPosition - center;
            float angle = offset.x * offset.x + offset.z * offset.z > 1e-6f ? Mathf.Atan2(offset.z, offset.x) : 0f;
            angle += leadDegrees * Mathf.Deg2Rad;
            return center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// Moves a circle centre so the whole circle (<paramref name="radius"/> + <paramref name="margin"/> on every
        /// horizontal side, <paramref name="margin"/> vertically) stays inside <paramref name="volume"/>.
        /// </summary>
        public static Vector3 ClampCircleToVolume(Vector3 center, float radius, Bounds volume, float margin)
        {
            float side = radius + margin;
            Vector3 min = volume.min;
            Vector3 max = volume.max;
            return new Vector3(
                min.x + side <= max.x - side ? Mathf.Clamp(center.x, min.x + side, max.x - side) : volume.center.x,
                Mathf.Clamp(center.y, min.y + margin, max.y - margin),
                min.z + side <= max.z - side ? Mathf.Clamp(center.z, min.z + side, max.z - side) : volume.center.z);
        }
    }
}
