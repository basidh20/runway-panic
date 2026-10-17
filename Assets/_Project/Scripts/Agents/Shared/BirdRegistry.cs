using System;
using System.Collections.Generic;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>What happened when a bird died, for species that react to deaths (crow funerals, grudges).</summary>
    public readonly struct BirdDeathInfo
    {
        /// <summary>Creates the record.</summary>
        public BirdDeathInfo(BirdHealth bird, BirdSpecies species, Vector3 position, Vector3 shotOrigin, float time)
        {
            Bird = bird;
            Species = species;
            Position = position;
            ShotOrigin = shotOrigin;
            Time = time;
        }

        /// <summary>The bird that died (destroyed a few seconds later).</summary>
        public BirdHealth Bird { get; }

        /// <summary>Its species.</summary>
        public BirdSpecies Species { get; }

        /// <summary>Where it died.</summary>
        public Vector3 Position { get; }

        /// <summary>Muzzle position of the shot that killed it (where the player stood).</summary>
        public Vector3 ShotOrigin { get; }

        /// <summary>Time.time of the death.</summary>
        public float Time { get; }
    }

    /// <summary>
    /// Every living bird, so brains can count and find other birds without searching the scene.
    /// <c>BirdHealth</c> registers itself while enabled and reports its death through <see cref="NotifyDied"/>.
    /// </summary>
    public static class BirdRegistry
    {
        private static readonly List<BirdHealth> alive = new List<BirdHealth>(128);

        /// <summary>Raised once per bird death.</summary>
        public static event Action<BirdDeathInfo> BirdDied;

        /// <summary>All registered living birds. Do not keep this across frames while iterating; birds come and go.</summary>
        public static IReadOnlyList<BirdHealth> Alive => alive;

        /// <summary>Adds a bird (ignored if already registered).</summary>
        public static void Register(BirdHealth bird)
        {
            if (bird != null && !alive.Contains(bird))
                alive.Add(bird);
        }

        /// <summary>Removes a bird (ignored if not registered).</summary>
        public static void Unregister(BirdHealth bird)
        {
            alive.Remove(bird);
        }

        /// <summary>Unregisters <paramref name="bird"/> and raises <see cref="BirdDied"/>. Called by BirdHealth on death.</summary>
        public static void NotifyDied(BirdHealth bird, Vector3 shotOrigin)
        {
            if (bird == null)
                return;

            Unregister(bird);
            BirdSpecies species = bird.Species != null ? bird.Species.Species : default;
            BirdDied?.Invoke(new BirdDeathInfo(bird, species, bird.transform.position, shotOrigin, UnityEngine.Time.time));
        }

        /// <summary>Number of living birds of <paramref name="species"/>.</summary>
        public static int CountAlive(BirdSpecies species)
        {
            int count = 0;
            for (int i = 0; i < alive.Count; i++)
                if (IsSpecies(alive[i], species))
                    count++;
            return count;
        }

        /// <summary>Clears <paramref name="results"/> and fills it with the living birds of <paramref name="species"/>.</summary>
        public static void GetAlive(BirdSpecies species, List<BirdHealth> results)
        {
            results.Clear();
            for (int i = 0; i < alive.Count; i++)
                if (IsSpecies(alive[i], species))
                    results.Add(alive[i]);
        }

        private static bool IsSpecies(BirdHealth bird, BirdSpecies species)
        {
            return bird != null && bird.Species != null && bird.Species.Species == species;
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            alive.Clear();
            BirdDied = null;
        }
    }
}
