using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Counters for the crow debug overlay and the viva notes (how often each behaviour actually happened).
    /// Plain static counters: incrementing them costs nothing, and they reset at the start of every Play session.
    /// </summary>
    public static class CrowStats
    {
        /// <summary>Approach points chosen (each one is a danger-aware decision).</summary>
        public static int Approaches;
        /// <summary>Contact attacks that hit the player.</summary>
        public static int AttackHits;
        /// <summary>Danger stamps written into the influence map (one per crow death).</summary>
        public static int DangerStamps;
        /// <summary>Thefts started (a crow claimed a pickup).</summary>
        public static int TheftsAttempted;
        /// <summary>Stolen pickups delivered to a stash.</summary>
        public static int TheftsSucceeded;
        /// <summary>Stolen pickups dropped because the thief was shot.</summary>
        public static int TheftsRecovered;
        /// <summary>Crows that joined a funeral.</summary>
        public static int FuneralAttendances;
        /// <summary>Times a crow became enraged.</summary>
        public static int Enrages;
        /// <summary>Times a crow started guarding a shelter exit.</summary>
        public static int Vigils;
        /// <summary>Times a crow dodged a shot that passed close.</summary>
        public static int Evasions;

        /// <summary>Sets every counter back to zero.</summary>
        public static void Reset()
        {
            Approaches = AttackHits = DangerStamps = 0;
            TheftsAttempted = TheftsSucceeded = TheftsRecovered = 0;
            FuneralAttendances = Enrages = Vigils = Evasions = 0;
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reset();
    }
}
