using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// How one species flies: how sharply it turns, how far it rolls into a turn, how hard it speeds up and how fast
    /// it flaps. Top speed is not here: it comes from the species' <see cref="Data.BirdSpeciesData.MoveSpeed"/>, so the
    /// gameplay stats and the motion feel stay in separate assets with separate owners.
    /// One asset per species in ScriptableObjects/Agents/ (Tools > Agents > Setup Bird Prefabs creates them).
    /// </summary>
    [CreateAssetMenu(fileName = "BirdMotion_New", menuName = "Runway Panic/Agents/Bird Motion Profile")]
    public sealed class BirdMotionProfile : ScriptableObject
    {
        [Header("Turning")]
        [Tooltip("Fastest change of flight direction, in degrees per second. Low = wide, smooth arcs.")]
        [SerializeField, Range(30f, 720f)] private float maxTurnRate = 150f;
        [Tooltip("Steepest climb or dive, in degrees from level.")]
        [SerializeField, Range(5f, 80f)] private float maxPitch = 35f;

        [Header("Banking")]
        [Tooltip("Largest roll into a turn, in degrees.")]
        [SerializeField, Range(0f, 80f)] private float maxBank = 35f;
        [Tooltip("Seconds the roll takes to catch up with the turn. Low = twitchy, high = smooth.")]
        [SerializeField, Range(0.01f, 1f)] private float bankSmoothTime = 0.15f;

        [Header("Speed")]
        [Tooltip("Speed gained or lost per second, in m/s². Also decides how early the bird brakes before the last waypoint.")]
        [SerializeField, Min(0.1f)] private float acceleration = 6f;
        [Tooltip("How far ahead along the smoothed path the bird aims, in metres. Larger = smoother, but cuts corners more.")]
        [SerializeField, Min(0.1f)] private float lookAheadDistance = 2f;

        [Header("Animation")]
        [Tooltip("Wing-beat speed multiplier (1 = the clip's own speed). Small birds flap faster.")]
        [SerializeField, Range(0.25f, 3f)] private float flapSpeedMultiplier = 1f;

        public float MaxTurnRate => maxTurnRate;
        public float MaxPitch => maxPitch;
        public float MaxBank => maxBank;
        public float BankSmoothTime => bankSmoothTime;
        public float Acceleration => acceleration;
        public float LookAheadDistance => lookAheadDistance;
        public float FlapSpeedMultiplier => flapSpeedMultiplier;
    }
}
