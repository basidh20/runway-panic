using System.Collections.Generic;
using RunwayPanic.Core;
using RunwayPanic.Interaction;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Shared bookkeeping for crow thefts: which crow has claimed which pickup (so two crows never chase the same
    /// one, and at most N steal at once), plus a cached list of the pickups in the scene so crows never search the
    /// scene every frame.
    /// </summary>
    public static class CrowPickupClaims
    {
        private const float CacheLifetime = 5f;

        private static readonly Dictionary<Pickup, CrowBrain> claims = new Dictionary<Pickup, CrowBrain>();
        private static readonly Dictionary<Pickup, float> unreachableUntil = new Dictionary<Pickup, float>();
        private static readonly List<Pickup> cache = new List<Pickup>(16);
        private static float cacheTime = float.NegativeInfinity;
        private static int cachedWave = -1;

        /// <summary>Crows currently holding a claim (flying to or carrying a pickup).</summary>
        public static int ThiefCount => claims.Count;

        /// <summary>True when some crow has claimed <paramref name="pickup"/>.</summary>
        public static bool IsClaimed(Pickup pickup) => pickup != null && claims.ContainsKey(pickup);

        /// <summary>
        /// Claims <paramref name="pickup"/> for <paramref name="crow"/>. Fails when it is already claimed or
        /// <paramref name="maxThieves"/> crows are already stealing.
        /// </summary>
        public static bool TryClaim(Pickup pickup, CrowBrain crow, int maxThieves)
        {
            if (pickup == null || crow == null || claims.ContainsKey(pickup) || claims.Count >= maxThieves)
                return false;

            claims.Add(pickup, crow);
            return true;
        }

        /// <summary>
        /// Remembers that no crow can reach <paramref name="pickup"/> (e.g. it lies inside a sealed shelter) until
        /// Time.time passes <paramref name="until"/>. Shared, so one failed search spares every other crow.
        /// </summary>
        public static void MarkUnreachable(Pickup pickup, float until)
        {
            if (pickup != null)
                unreachableUntil[pickup] = until;
        }

        /// <summary>True while <paramref name="pickup"/> is remembered as unreachable.</summary>
        public static bool IsUnreachable(Pickup pickup) =>
            pickup != null && unreachableUntil.TryGetValue(pickup, out float until) && Time.time < until;

        /// <summary>Releases the claim on <paramref name="pickup"/> (ignored if none).</summary>
        public static void Release(Pickup pickup)
        {
            if (pickup != null)
                claims.Remove(pickup);
            PruneDestroyed();
        }

        /// <summary>
        /// The pickups currently in the scene, refreshed at most every few seconds and when a new wave starts
        /// (the pickup spawner refills then). The list is shared: read it, don't keep it.
        /// </summary>
        public static IReadOnlyList<Pickup> Pickups
        {
            get
            {
                int wave = GameManager.Instance != null ? GameManager.Instance.CurrentWave : 0;
                if (Time.time - cacheTime > CacheLifetime || wave != cachedWave)
                {
                    cache.Clear();
                    cache.AddRange(Object.FindObjectsByType<Pickup>());
                    cacheTime = Time.time;
                    cachedWave = wave;
                }

                return cache;
            }
        }

        // Claims on pickups that were destroyed (collected by the player) are dropped.
        private static readonly List<Pickup> toRemove = new List<Pickup>(4);

        private static void PruneDestroyed()
        {
            toRemove.Clear();
            foreach (KeyValuePair<Pickup, CrowBrain> pair in claims)
                if (pair.Key == null || pair.Value == null)
                    toRemove.Add(pair.Key);
            foreach (Pickup p in toRemove)
                claims.Remove(p);

            toRemove.Clear();
            foreach (KeyValuePair<Pickup, float> pair in unreachableUntil)
                if (pair.Key == null)
                    toRemove.Add(pair.Key);
            foreach (Pickup p in toRemove)
                unreachableUntil.Remove(p);
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            claims.Clear();
            unreachableUntil.Clear();
            cache.Clear();
            cacheTime = float.NegativeInfinity;
            cachedWave = -1;
        }
    }
}
