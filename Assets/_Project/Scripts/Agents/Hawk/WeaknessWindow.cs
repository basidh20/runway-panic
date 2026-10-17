namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// A moment when the player is less able to defend themselves. <see cref="PlayerWeaknessModel"/> tracks which are
    /// open; open windows fill the hawk's aggression meter, and the hawk learns which ones this player opens most.
    /// </summary>
    public enum WeaknessWindow
    {
        /// <summary>The held gun is reloading.</summary>
        Reloading,

        /// <summary>The player just switched weapon.</summary>
        SwitchingWeapon,

        /// <summary>The player just opened or closed a door.</summary>
        InteractingDoor,

        /// <summary>The player is pushing a barricade.</summary>
        PushingBarricade,

        /// <summary>The player's shield just ran out.</summary>
        ShieldExpired,

        /// <summary>The player has been looking away from the hawk for a while.</summary>
        LookingAway,

        /// <summary>The player's health is low.</summary>
        LowHealth,

        /// <summary>The player is busy shooting other birds (only counted while the hawk herds).</summary>
        Busy
    }
}
