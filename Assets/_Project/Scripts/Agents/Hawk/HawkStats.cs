namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// Counters of one hawk's play session (H7), shown by <see cref="HawkDebugOverlay"/> and written to the log as one
    /// <c>[HawkStats]</c> line when the hawk dies or is removed — the measured numbers for <c>Docs/IS/Hawk/NOTES.md</c>.
    /// </summary>
    public sealed class HawkStats
    {
        /// <summary>Real dives started (from the sun approach, after a feint, or from Herd).</summary>
        public int Stoops { get; internal set; }

        /// <summary>Real dives that reached the player.</summary>
        public int StoopHits { get; internal set; }

        /// <summary>Feints flown (fake dives that pull up short).</summary>
        public int Feints { get; internal set; }

        /// <summary>Feints during which the player fired (the bait worked).</summary>
        public int FeintsBaited { get; internal set; }

        /// <summary>Real dives that started right after a feint.</summary>
        public int StoopsAfterFeint { get; internal set; }

        /// <summary>Of those, dives that reached the player.</summary>
        public int HitsAfterFeint { get; internal set; }

        /// <summary>Dives started straight from Herd mode.</summary>
        public int HerdStrikes { get; internal set; }

        /// <summary>Non-lethal hits taken.</summary>
        public int HitsTaken { get; internal set; }

        /// <summary>Times the hawk withdrew to a hidden vantage.</summary>
        public int Withdrawals { get; internal set; }

        /// <summary>Times the hawk switched into Herd mode.</summary>
        public int HerdEntries { get; internal set; }

        /// <summary>Attacks started because the meter was full and a window open (the rest: max watch time).</summary>
        public int MeterAttacks { get; internal set; }

        /// <summary>Attacks started by the max watch time (the player was never weak enough).</summary>
        public int TimeoutAttacks { get; internal set; }

        /// <summary>Total seconds spent soaring over a vantage before attacking.</summary>
        public float WatchSeconds { get; internal set; }

        /// <summary>Hit rate of all real dives, 0..1 (0 when there were none).</summary>
        public float HitRate => Stoops > 0 ? (float)StoopHits / Stoops : 0f;

        /// <summary>Hit rate of dives right after a feint, 0..1.</summary>
        public float HitRateAfterFeint => StoopsAfterFeint > 0 ? (float)HitsAfterFeint / StoopsAfterFeint : 0f;

        /// <summary>Hit rate of dives without a feint before them, 0..1.</summary>
        public float HitRateWithoutFeint
        {
            get
            {
                int stoops = Stoops - StoopsAfterFeint;
                return stoops > 0 ? (float)(StoopHits - HitsAfterFeint) / stoops : 0f;
            }
        }

        /// <summary>Average seconds of soaring before an attack.</summary>
        public float AverageWatchSeconds
        {
            get
            {
                int attacks = MeterAttacks + TimeoutAttacks;
                return attacks > 0 ? WatchSeconds / attacks : 0f;
            }
        }

        /// <summary>One line for the log.</summary>
        public override string ToString()
        {
            return $"stoops {Stoops} (hits {StoopHits}, {HitRate:P0}); after feint {StoopsAfterFeint} (hits {HitsAfterFeint}, {HitRateAfterFeint:P0}); " +
                   $"without feint {HitRateWithoutFeint:P0}; feints {Feints} (baited {FeintsBaited}); herd strikes {HerdStrikes}; " +
                   $"hits taken {HitsTaken}; withdrawals {Withdrawals}; herd entries {HerdEntries}; " +
                   $"attacks by meter {MeterAttacks} / by timeout {TimeoutAttacks}; average watch {AverageWatchSeconds:0.0}s";
        }
    }
}
