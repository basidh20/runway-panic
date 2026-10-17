using RunwayPanic.AI;
using UnityEngine;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// Pure maths for the hawk's stoop (dive): where the dive starts so it comes out of the sun, and where to aim so it
    /// meets a moving player. No state, no MonoBehaviour.
    /// <para>Stoop start = <c>player − sunForward × distance</c> (sunForward = the direction sunlight travels, so
    /// −sunForward points at the sun), raised to at least <c>minHeight</c> above the player. A player looking at the
    /// diving hawk has to look into the sun.</para>
    /// </summary>
    public static class StoopPlanner
    {
        /// <summary>
        /// Stoop start point <paramref name="distance"/> metres from <paramref name="playerPosition"/> towards the sun,
        /// at least <paramref name="minHeight"/> above the player. <paramref name="yawDegrees"/> turns the direction
        /// around the vertical axis (0 = exactly sun-side), used to step around buildings that block the dive line.
        /// </summary>
        public static Vector3 GetStoopStart(Vector3 playerPosition, Vector3 sunForward, float distance, float minHeight, float yawDegrees)
        {
            Vector3 towardSun = Quaternion.AngleAxis(yawDegrees, Vector3.up) * -sunForward.normalized;
            Vector3 start = playerPosition + towardSun * distance;
            start.y = Mathf.Max(start.y, playerPosition.y + minHeight);
            return start;
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
    }
}
