using UnityEngine;

namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>
    /// Every tuning value for the pigeon strategy ("The Panicking Flock") in one asset, so behaviour can be balanced
    /// without code changes. Grouped by the part of the strategy it controls; each phase adds its own group together
    /// with the code that reads it, so no value exists before there is a reason for it.
    /// </summary>
    [CreateAssetMenu(fileName = "PigeonStrategyConfig", menuName = "Runway Panic/AI/Pigeon Strategy")]
    public sealed class PigeonStrategyConfig : ScriptableObject
    {
        [Header("Flock membership")]
        [Tooltip("Most pigeons one flock coordinates; a pigeon that finds the flock full fights alone (Solo). " +
                 "The waves never have more than 6 pigeons alive at once; 12 leaves room for the stress test while " +
                 "keeping the neighbour checks (every member against every other) small.")]
        [Range(2, 24)] public int maxFlockSize = 12;

        [Header("Debug")]
        [Tooltip("Log flock joins, leaves and mode changes to the Console (Editor and development builds only). " +
                 "Events are rare (a few per wave), so this costs nothing while flying.")]
        public bool logFlockEvents = true;

        [Header("Formation (metres and seconds)")]
        [Min(1f)] public float neighbourRadius = 6f;
        [Min(0.2f)] public float separationRadius = 2f;
        [Min(0f)] public float separationWeight = 2f;
        [Min(0f)] public float alignmentWeight = 0.35f;
        [Min(0f)] public float cohesionWeight = 0.25f;
        [Min(0.1f)] public float steeringLimit = 2f;
        [Min(2f)] public float rallyRadius = 10f;
        [Min(2f)] public float flightHeight = 7f;
        [Min(1f)] public float formationRadius = 4f;
        [Min(0.1f)] public float repathSeconds = 1.2f;
        [Min(1f)] public float gatherTimeout = 8f;
        [Range(0.1f, 1f)] public float readyFraction = 0.6f;

        [Header("Attacks")]
        [Range(1, 4)] public int maxDivers = 2;
        [Min(0.2f)] public float diveSpacing = 1.2f;
        [Min(1f)] public float diveTimeout = 5f;
        [Min(1f)] public float diveSpeed = 1.8f;
        [Min(1f)] public float recoverSeconds = 3f;
        [Min(1f)] public float mobSeconds = 14f;
        [Min(1f)] public float soloCooldown = 5f;

        [Header("Panic (normalised alarm)")]
        [Min(1f)] public float threatRadius = 12f;
        [Range(0f, 1f)] public float shotAlarm = 0.2f;
        [Range(0f, 1f)] public float damageAlarm = 0.45f;
        [Range(0f, 1f)] public float deathAlarm = 0.65f;
        [Range(0.1f, 1f)] public float panicThreshold = 0.7f;
        [Min(0.01f)] public float alarmDecay = 0.08f;
        [Min(1f)] public float scatterSeconds = 4f;
        [Min(1f)] public float panicCooldown = 8f;
        [Min(1f)] public float roostTimeout = 6f;
    }
}
