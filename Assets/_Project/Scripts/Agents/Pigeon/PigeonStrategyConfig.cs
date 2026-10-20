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
    }
}
