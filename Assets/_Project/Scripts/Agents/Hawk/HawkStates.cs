namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// Top level of the hawk's hierarchical state machine: what the hawk is doing overall.
    /// <see cref="Hunt"/> has its own sub-states (<see cref="HuntPhase"/>); the other modes have none.
    /// </summary>
    public enum HawkMode
    {
        /// <summary>Soar over a vantage point, watch the player, then stoop. Sub-states in <see cref="HuntPhase"/>.</summary>
        Hunt,

        /// <summary>Circle behind other birds and push them at the player with a fear field (mixed waves).</summary>
        Herd,

        /// <summary>Wounded: hide where the player can't see, then hunt again.</summary>
        Withdraw,

        /// <summary>Killed. Final state.</summary>
        Dead
    }

    /// <summary>
    /// Sub-states of <see cref="HawkMode.Hunt"/>, in the order of one attack cycle. The hawk never sits still: it watches
    /// the player while circling above a vantage point (a <see cref="BirdPoint"/> of type Perch).
    /// </summary>
    public enum HuntPhase
    {
        /// <summary>Score every vantage point and pick the best one.</summary>
        SelectVantage,

        /// <summary>Fly to the soaring circle above the chosen vantage point.</summary>
        FlyToVantage,

        /// <summary>Circle above the vantage point and read the player until the aggression meter fills.</summary>
        Soaring,

        /// <summary>Fly to the stoop start point on the sun's side of the player.</summary>
        ApproachStoop,

        /// <summary>Hover briefly at the top of the dive and scream (the audio tell).</summary>
        Scream,

        /// <summary>Fake dive that pulls up short, to bait a shot from a slow gun.</summary>
        Feint,

        /// <summary>The real dive at the player.</summary>
        Stoop,

        /// <summary>Climb away after the attack, then select a vantage point again.</summary>
        Recover
    }
}
