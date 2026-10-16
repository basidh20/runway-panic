using System;
using UnityEngine;

namespace RunwayPanic.Data
{
    /// <summary>
    /// Configuration for one wave: which birds spawn, how many, and for how long the wave runs.
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
        [Tooltip("Countdown length in seconds. Surviving it ends the wave.")]
        [SerializeField, Min(1f)] private float duration = 60f;
        [Tooltip("Seconds between individual bird spawns.")]
        [SerializeField, Min(0f)] private float spawnInterval = 2f;
        [SerializeField] private SpawnGroup[] spawnGroups = Array.Empty<SpawnGroup>();

        public int WaveNumber => waveNumber;
        public float Duration => duration;
        public float SpawnInterval => spawnInterval;
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
    }
}
