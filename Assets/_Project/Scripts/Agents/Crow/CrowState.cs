namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// States of the crow's finite state machine. Goal priority when several apply:
    /// Funeral &gt; Steal &gt; Attack, and Enraged overrides caution (ignores the danger map).
    /// </summary>
    public enum CrowState
    {
        /// <summary>Wanders between random flyable points.</summary>
        Patrol = 0,
        /// <summary>Flies to an approach point chosen around the player, routing around remembered danger.</summary>
        Approach = 1,
        /// <summary>Dives at the player and tries one contact attack.</summary>
        Attack = 2,
        /// <summary>Breaks off to the far side after attacking, then approaches again from a new angle.</summary>
        Retreat = 3,
        /// <summary>Flies to a pickup it has claimed.</summary>
        StealFly = 4,
        /// <summary>Carries the stolen pickup to a stash.</summary>
        CarryToStash = 5,
        /// <summary>Circles a dead crow (vulnerable).</summary>
        Funeral = 6,
        /// <summary>Fast, direct attacks after a funeral; ignores danger.</summary>
        Enraged = 7,
        /// <summary>Perches at a shelter exit while the player hides inside.</summary>
        Vigil = 8,
        /// <summary>Dead; the brain stops.</summary>
        Dead = 9
    }
}
