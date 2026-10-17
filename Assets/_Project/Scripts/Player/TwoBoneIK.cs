using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Analytic two-bone IK (upper arm → forearm → hand): bends a limb so its end reaches a target, with the middle
    /// joint (elbow) bending towards a pole point. Used at runtime to put the left hand on the gun and by the editor
    /// clip generator to build the weapon poses, so both place hands with the same maths.
    /// </summary>
    public static class TwoBoneIK
    {
        private const float MinLength = 1e-4f;

        /// <summary>
        /// Rotates <paramref name="upper"/> and <paramref name="lower"/> so <paramref name="end"/> reaches
        /// <paramref name="target"/> (or as close as the limb's length allows), elbow towards <paramref name="pole"/>.
        /// <paramref name="weight"/> blends from the current pose (0) to the solved pose (1).
        /// </summary>
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole,
            float weight = 1f)
        {
            if (upper == null || lower == null || end == null || weight <= 0f)
                return;

            Vector3 a = upper.position;
            float upperLength = Vector3.Distance(a, lower.position);
            float lowerLength = Vector3.Distance(lower.position, end.position);
            Vector3 toTarget = target - a;
            float distance = toTarget.magnitude;
            if (upperLength < MinLength || lowerLength < MinLength || distance < MinLength)
                return;

            // Law of cosines: angle at the shoulder between the shoulder→target line and the upper bone.
            distance = Mathf.Clamp(distance, Mathf.Abs(upperLength - lowerLength) + MinLength,
                upperLength + lowerLength - MinLength);
            float cosShoulder = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                                (2f * upperLength * distance);
            float sinShoulder = Mathf.Sqrt(Mathf.Max(0f, 1f - cosShoulder * cosShoulder));

            Vector3 direction = toTarget.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole - a, direction);
            if (bend.sqrMagnitude < MinLength)
                bend = Vector3.ProjectOnPlane(lower.position - a, direction);
            bend = bend.sqrMagnitude > MinLength ? bend.normalized : Vector3.Cross(direction, Vector3.right).normalized;

            Vector3 elbow = a + direction * (upperLength * cosShoulder) + bend * (upperLength * sinShoulder);
            Vector3 reach = a + direction * distance;

            Quaternion upperSolved = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
            upper.rotation = Quaternion.Slerp(upper.rotation, upperSolved, weight);

            Quaternion lowerSolved = Quaternion.FromToRotation(end.position - lower.position, reach - lower.position) *
                                     lower.rotation;
            lower.rotation = Quaternion.Slerp(lower.rotation, lowerSolved, weight);
        }
    }
}
