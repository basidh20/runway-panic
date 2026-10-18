namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>
    /// States of the flock-level state machine run by <see cref="PigeonFlock"/>. One mode for the whole group, so the
    /// flock acts as one organism: gather, mob, panic, regroup, and come back from a new side.
    /// </summary>
    public enum PigeonFlockMode
    {
        /// <summary>No members, or settled on a roost with nothing to attack (no player yet, player dead).</summary>
        Roosting = 0,
        /// <summary>Scattered spawns converge on one gather point (a roost or a rally point).</summary>
        Gathering = 1,
        /// <summary>Orbits the rally point and sends staggered divers at the player.</summary>
        Mobbing = 2,
        /// <summary>Panic has spread through the flock: every bird flees.</summary>
        Scattering = 3,
        /// <summary>Reforms at a newly chosen rally point before mobbing again.</summary>
        Regrouping = 4
    }
}
