using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Sandbox tool: press the spawn key (default <b>B</b>) to spawn birds at random <see cref="BirdPointType.Spawn"/>
    /// markers, or at this object when there are none. Not for the real game (the wave manager spawns there).
    /// </summary>
    public sealed class BirdTestSpawner : MonoBehaviour
    {
        [Tooltip("Bird prefab to spawn, e.g. P_Bird_Hawk.")]
        [SerializeField] private GameObject prefab;
        [Tooltip("Birds spawned per key press.")]
        [SerializeField, Min(1)] private int count = 1;
        [Tooltip("Key that spawns the birds.")]
        [SerializeField] private Key spawnKey = Key.B;
        [Tooltip("Also spawn once when Play starts.")]
        [SerializeField] private bool spawnOnStart;

        private readonly List<BirdPoint> spawnPoints = new List<BirdPoint>(16);

        private void Start()
        {
            if (spawnOnStart)
                Spawn();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[spawnKey].wasPressedThisFrame)
                Spawn();
        }

        /// <summary>Spawns <c>count</c> birds now.</summary>
        public void Spawn()
        {
            if (prefab == null)
            {
                Debug.LogWarning($"{nameof(BirdTestSpawner)} on '{name}' has no prefab.", this);
                return;
            }

            BirdPoint.GetAll(BirdPointType.Spawn, spawnPoints);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = transform.position;
                float scatter = 1f;
                if (spawnPoints.Count > 0)
                {
                    BirdPoint point = spawnPoints[Random.Range(0, spawnPoints.Count)];
                    position = point.Position;
                    scatter = point.Radius;
                }

                position += Random.insideUnitSphere * scatter;
                Instantiate(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
        }
    }
}
