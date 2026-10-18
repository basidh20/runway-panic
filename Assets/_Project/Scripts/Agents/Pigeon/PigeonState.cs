namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>
    /// States of one pigeon's finite state machine. The flock's own state machine (<see cref="PigeonFlockMode"/>)
    /// decides what the group is doing; a bird's state is the part it plays in that.
    /// </summary>
    public enum PigeonState
    {
        /// <summary>Just spawned (or re-enabled): joins the flock, then takes the role the flock gives it.</summary>
        JoinFlock = 0,
        /// <summary>Flies an A* leg to a roost's approach point, then lands on its claimed slot.</summary>
        FlyToRoost = 1,
        /// <summary>Perched on a roost slot (or hovering beside it after the landing timeout).</summary>
        Roosting = 2,
        /// <summary>Flies with the flock (boids) around the gather or rally point.</summary>
        Flocking = 3,
        /// <summary>Holds a dive token: flies at the player's chest and tries one contact hit.</summary>
        Dive = 4,
        /// <summary>After a dive: breaks away to the far side of the player, then rejoins the flock.</summary>
        Recover = 5,
        /// <summary>Alarm above the threshold: flees away from the threat.</summary>
        Panic = 6,
        /// <summary>No flock to fly with (the last pigeon, or the flock is full): hit-and-run from cover.</summary>
        Solo = 7,
        /// <summary>Dead; the brain stops.</summary>
        Dead = 8
    }
}
