using System;
using UnityEngine;

namespace RunwayPanic.Data
{
    /// <summary>
    /// Configuration for one wave: which birds spawn, how many, and for how long the wave runs. Read by the
    /// WaveManager; the title and hint are shown on the HUD banner before the wave.
    /// </summary>
    [CreateAssetMenu(fileName = "Wave_New", menuName = "Runway Panic/Data/Wave")]
    public sealed class WaveData : ScriptableObject
    {
        [Serializable]
        public struct SpawnGroup
        {
            public BirdSpeciesData species;
            [Min(0)] public int count;
        }

        [SerializeField, Min(1)] private int waveNumber = 1;
        [Tooltip("Shown on the HUD banner, e.g. \"Gulls\".")]
        [SerializeField] private string title = "Birds";
        [Tooltip("One line of advice under the title, e.g. which gun to use.")]
        [SerializeField] private string hint = "";
        [Tooltip("Countdown length in seconds. Surviving it ends the wave.")]
        [SerializeField, Min(1f)] private float duration = 60f;
        [Tooltip("Seconds between individual bird spawns.")]
        [SerializeField, Min(0f)] private float spawnInterval = 2f;
        [Tooltip("Seconds after the wave starts before the first bird appears.")]
        [SerializeField, Min(0f)] private float firstSpawnDelay = 1f;
        [Tooltip("At most this many of this wave's birds alive at once; the rest wait their turn.")]
        [SerializeField, Min(1)] private int maxAlive = 5;
        [Tooltip("Coins for killing every bird before the timer ends.")]
        [SerializeField, Min(0)] private int clearBonus = 10;
        [SerializeField] private SpawnGroup[] spawnGroups = Array.Empty<SpawnGroup>();

        public int WaveNumber => waveNumber;
        public string Title => title;
        public string Hint => hint;
        public float Duration => duration;
        public float SpawnInterval => spawnInterval;
        public float FirstSpawnDelay => firstSpawnDelay;
        public int MaxAlive => maxAlive;
        public int ClearBonus => clearBonus;
        public SpawnGroup[] SpawnGroups => spawnGroups;

        public int TotalBirdCount
        {
            get
            {
                int total = 0;
                foreach (SpawnGroup group in spawnGroups)
                    total += group.count;
                return total;
            }
        }

        /// <summary>Coins for killing every bird, without the clear bonus.</summary>
        public int TotalKillCoins
        {
            get
            {
                int total = 0;
                foreach (SpawnGroup group in spawnGroups)
                    if (group.species != null)
                        total += group.species.CoinReward * group.count;
                return total;
            }
        }
    }
}
