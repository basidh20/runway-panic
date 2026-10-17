using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Every tuning value for the crow strategy ("The Thief With a Grudge") in one asset,
    /// so behaviour can be balanced without code changes. Grouped by the part of the strategy it controls.
    /// </summary>
    [CreateAssetMenu(fileName = "CrowStrategyConfig", menuName = "Runway Panic/AI/Crow Strategy")]
    public sealed class CrowStrategyConfig : ScriptableObject
    {
        [Header("Grudge: danger influence map")]
        [Tooltip("Size of one influence-map cell in metres. Smaller = finer detail, more cells to update.")]
        [Min(0.5f)] public float dangerCellSize = 2f;
        [Tooltip("Radius in metres of the danger stamped around the shooter when a crow dies.")]
        [Min(0.5f)] public float dangerMarkRadius = 6f;
        [Tooltip("Half-width in metres of the danger stripe stamped along the line of fire.")]
        [Min(0.1f)] public float dangerLineRadius = 2f;
        [Tooltip("Danger added at the centre of a stamp per crow death. Cells are clamped to Max Danger.")]
        [Min(0f)] public float dangerMarkStrength = 1f;
        [Tooltip("Upper limit for a single cell, so repeated deaths cannot create an impassable wall.")]
        [Min(0.1f)] public float maxDanger = 5f;
        [Tooltip("Extra A* cost per unit of danger. Higher = crows detour further to avoid remembered danger.")]
        [Min(0f)] public float dangerCostScale = 20f;
        [Tooltip("Seconds between decay steps.")]
        [Min(0.1f)] public float dangerDecayInterval = 5f;
        [Tooltip("Each decay step multiplies every cell by this. 0.9 every 5 s gives a half-life of about 33 s.")]
        [Range(0f, 1f)] public float dangerDecayFactor = 0.9f;
        [Tooltip("Fraction of the danger map kept when a new wave starts.")]
        [Range(0f, 1f)] public float dangerKeptOnNewWave = 0.5f;

        [Header("Approach")]
        [Tooltip("Distance from the player at which crows detect them and stop patrolling.")]
        [Min(1f)] public float detectRange = 30f;
        [Tooltip("Radius of the ring of candidate approach points around the player.")]
        [Min(1f)] public float approachRingRadius = 6f;
        [Tooltip("How many candidate approach points are scored around the ring.")]
        [Range(3, 32)] public int approachSamples = 8;
        [Tooltip("Height above the player of the approach points.")]
        [Min(0f)] public float approachHeight = 2f;
        [Tooltip("Distance a crow flies away from the player after an attack before approaching again.")]
        [Min(1f)] public float retreatDistance = 10f;

        [Header("Patrol")]
        [Tooltip("Shortest and longest distance between patrol waypoints.")]
        [Min(1f)] public float patrolMinDistance = 10f;
        [Min(1f)] public float patrolMaxDistance = 20f;
        [Tooltip("Lowest and highest patrol flying height above the ground.")]
        [Min(0.5f)] public float patrolMinHeight = 4f;
        [Min(0.5f)] public float patrolMaxHeight = 8f;

        [Header("Theft")]
        [Tooltip("Seconds between scans for pickups to steal.")]
        [Min(0.1f)] public float stealScanInterval = 1f;
        [Tooltip("Furthest a pickup can be for a crow to try stealing it.")]
        [Min(1f)] public float stealMaxDistance = 40f;
        [Tooltip("Crows won't snatch a pickup closer than this to the player.")]
        [Min(0f)] public float minPlayerDistanceToSteal = 4f;
        [Tooltip("Most crows allowed to be stealing at the same time.")]
        [Min(0)] public int maxConcurrentThieves = 2;

        [Header("Funeral and rage")]
        [Tooltip("Crows within this distance of a dead crow gather for the funeral.")]
        [Min(0f)] public float funeralRadius = 15f;
        [Tooltip("Seconds spent circling the body (the player's window to strike).")]
        [Min(0f)] public float funeralDuration = 3f;
        [Min(0.5f)] public float funeralOrbitRadius = 2.5f;
        [Min(0f)] public float funeralOrbitHeight = 3f;
        [Tooltip("Speed multiplier while enraged.")]
        [Min(1f)] public float enragedSpeedMultiplier = 1.4f;
        [Tooltip("Seconds the rage lasts.")]
        [Min(0f)] public float enragedDuration = 10f;

        [Header("Vigil (optional)")]
        [Tooltip("Seconds the player must stay inside a shelter before crows start guarding its exits.")]
        [Min(0f)] public float vigilEnterDelay = 1f;
        [Min(0)] public int maxVigilCrows = 3;
        [Tooltip("Seconds a crow tries to land at its shelter exit before it stops and hovers next to it instead. " +
                 "Works around BirdMotor.LandAt circling its point when the bird arrives from the side.")]
        [Min(0.5f)] public float vigilLandTimeout = 3f;

        [Header("Evasion")]
        [Tooltip("A shot passing within this distance of an approaching or attacking crow makes it dodge.")]
        [Min(0.1f)] public float evadeRadius = 3f;
        [Tooltip("How far sideways (away from the shot) the crow jinks, in metres.")]
        [Min(0.5f)] public float evadeDistance = 4f;
        [Tooltip("Speed multiplier while dodging.")]
        [Min(1f)] public float evadeSpeedMultiplier = 1.5f;
        [Tooltip("Longest a dodge lasts before the crow approaches again.")]
        [Min(0.1f)] public float evadeDuration = 0.7f;
        [Tooltip("Seconds before the same crow can dodge again.")]
        [Min(0f)] public float evadeCooldown = 2.5f;

        [Header("Pathing")]
        [Tooltip("Seconds between path recalculations, so many crows don't search every frame.")]
        [Min(0.1f)] public float repathInterval = 0.75f;

        /// <summary>
        /// Half-life of remembered danger in seconds: the time for a cell to fall to half its value.
        /// Solves factor^(t / interval) = 0.5, i.e. t = interval * ln(0.5) / ln(factor).
        /// </summary>
        public float DangerHalfLife =>
            dangerDecayFactor <= 0f || dangerDecayFactor >= 1f
                ? (dangerDecayFactor >= 1f ? float.PositiveInfinity : 0f)
                : dangerDecayInterval * Mathf.Log(0.5f) / Mathf.Log(dangerDecayFactor);

        private void OnValidate()
        {
            patrolMaxDistance = Mathf.Max(patrolMinDistance, patrolMaxDistance);
            patrolMaxHeight = Mathf.Max(patrolMinHeight, patrolMaxHeight);
        }
    }
}
