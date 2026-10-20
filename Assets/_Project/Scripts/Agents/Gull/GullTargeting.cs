using UnityEngine;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>Constant horizontal-velocity lead; the warning and the free fall both contribute to lead time.</summary>
    public static class GullTargeting
    {
        public static Vector3 PredictDropPoint(Vector3 playerPos, Vector3 playerVel, float dropHeight, float telegraph, float gravity)
        {
            float fallTime = Mathf.Sqrt(2f * Mathf.Max(0f, dropHeight) / Mathf.Max(.01f, Mathf.Abs(gravity)));
            Vector3 horizontalVelocity = new Vector3(playerVel.x, 0, playerVel.z);
            Vector3 lead = Vector3.ClampMagnitude(horizontalVelocity * (fallTime + Mathf.Max(0f, telegraph)), 8f);
            return playerPos + lead;
        }
    }
}
