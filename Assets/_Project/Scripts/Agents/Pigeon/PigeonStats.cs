using UnityEngine;

namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>
    /// Counters for the pigeon debug overlay and the viva notes (how often each behaviour actually happened).
    /// Plain static counters: incrementing them costs nothing, and they reset at the start of every Play session.
    /// </summary>
    public static class PigeonStats
    {
        /// <summary>Pigeons that joined the flock (one per spawn, plus re-joins after a re-enable).</summary>
        public static int Joins;
        /// <summary>Pigeons that left the flock (death, despawn, disable). Joins − Leaves = current members.</summary>
        public static int Leaves;
        /// <summary>Pigeons killed.</summary>
        public static int Deaths;
        /// <summary>Living pigeons removed without dying (the WaveManager's time-up cleanup, tests).</summary>
        public static int Despawns;
        /// <summary>Changes of the flock-level state machine.</summary>
        public static int FlockModeChanges;

        /// <summary>Sets every counter back to zero.</summary>
        public static void Reset()
        {
            Joins = Leaves = Deaths = Despawns = FlockModeChanges = 0;
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reset();
    }
}
