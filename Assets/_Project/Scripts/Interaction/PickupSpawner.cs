using System;
using System.Collections;
using System.Collections.Generic;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Interaction
{
    /// <summary>
    /// Keeps a few medkits and shields lying around the level at random marker positions.
    /// A collected pickup is replaced at a random free marker after a delay, and every wave start refills
    /// the level straight away. With no markers assigned it uses the children of the World Builder's
    /// "PickupPoints" object.
    /// </summary>
    public sealed class PickupSpawner : MonoBehaviour
    {
        [Serializable]
        private struct WeightedPickup
        {
            public Pickup prefab;
            [Min(0)] public int weight;
        }

        [Header("Where")]
        [Tooltip("Spawn markers. Leave empty to use the children of the object named below.")]
        [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();
        [SerializeField] private string markerGroupName = "PickupPoints";

        [Header("What")]
        [SerializeField] private WeightedPickup[] pickups = Array.Empty<WeightedPickup>();

        [Header("How many and how often")]
        [SerializeField, Min(0)] private int maxActive = 2;
        [Tooltip("Seconds before a collected pickup is replaced.")]
        [SerializeField, Min(0f)] private float respawnDelay = 25f;
        [SerializeField] private bool refillOnWaveStart = true;

        private readonly Dictionary<Transform, Pickup> occupied = new Dictionary<Transform, Pickup>();

        public int ActiveCount => occupied.Count;

        private void Awake()
        {
            if (spawnPoints.Length == 0)
            {
                GameObject group = GameObject.Find(markerGroupName);
                if (group != null)
                {
                    var found = new List<Transform>();
                    foreach (Transform child in group.transform)
                        found.Add(child);
                    spawnPoints = found.ToArray();
                }
            }

            if (spawnPoints.Length == 0)
                Debug.LogWarning($"{nameof(PickupSpawner)} on '{name}' has no spawn points and found no '{markerGroupName}' object.", this);
        }

        private void Start()
        {
            if (refillOnWaveStart && GameManager.Instance != null)
                GameManager.Instance.WaveStarted += HandleWaveStarted;

            Refill();
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.WaveStarted -= HandleWaveStarted;
        }

        /// <summary>Spawns pickups until <see cref="maxActive"/> are lying in the level (or no free marker is left).</summary>
        public void Refill()
        {
            while (occupied.Count < maxActive && TrySpawnOne()) { }
        }

        private void HandleWaveStarted(int wave) => Refill();

        private bool TrySpawnOne()
        {
            Pickup prefab = PickRandomPrefab();
            Transform point = PickRandomFreePoint();
            if (prefab == null || point == null)
                return false;

            Pickup pickup = Instantiate(prefab, GroundPosition(point.position), point.rotation, transform);
            occupied.Add(point, pickup);
            pickup.Collected += collected => HandleCollected(point);
            return true;
        }

        private void HandleCollected(Transform point)
        {
            occupied.Remove(point);
            StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);

            // A wave-start refill may already have filled the level in the meantime.
            if (occupied.Count < maxActive)
                TrySpawnOne();
        }

        private Pickup PickRandomPrefab()
        {
            int total = 0;
            foreach (WeightedPickup entry in pickups)
            {
                if (entry.prefab != null)
                    total += entry.weight;
            }

            if (total <= 0)
                return null;

            int roll = UnityEngine.Random.Range(0, total);
            foreach (WeightedPickup entry in pickups)
            {
                if (entry.prefab == null)
                    continue;
                if (roll < entry.weight)
                    return entry.prefab;
                roll -= entry.weight;
            }

            return null;
        }

        private Transform PickRandomFreePoint()
        {
            var free = new List<Transform>();
            foreach (Transform point in spawnPoints)
            {
                if (point != null && !occupied.ContainsKey(point))
                    free.Add(point);
            }

            return free.Count > 0 ? free[UnityEngine.Random.Range(0, free.Count)] : null;
        }

        private static Vector3 GroundPosition(Vector3 marker)
        {
            // Markers may float a little; drop the pickup onto whatever solid floor is below.
            return Physics.Raycast(marker + Vector3.up, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : marker;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
            foreach (Transform point in spawnPoints)
            {
                if (point != null)
                    Gizmos.DrawWireSphere(point.position + Vector3.up * 0.5f, 0.5f);
            }
        }
    }
}
