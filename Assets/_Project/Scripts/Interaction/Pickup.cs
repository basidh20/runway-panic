using System;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Interaction
{
    /// <summary>
    /// Base for items the player collects by walking over them (medkits, shields).
    /// The root holds a trigger collider; the <see cref="visual"/> child spins and bobs so pickups are easy to spot.
    /// If the player cannot take it yet (e.g. full HP and a full inventory), it stays and is retried while they stand on it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public abstract class Pickup : MonoBehaviour
    {
        [Header("Presentation")]
        [Tooltip("Child that spins and bobs. The trigger on the root stays still.")]
        [SerializeField] private Transform visual;
        [SerializeField] private float spinSpeed = 90f;
        [SerializeField, Min(0f)] private float bobHeight = 0.12f;
        [SerializeField, Min(0f)] private float bobSpeed = 2f;

        /// <summary>Raised when the player collects it, just before it is removed.</summary>
        public event Action<Pickup> Collected;

        private Vector3 visualRestPosition;
        private float bobOffset;
        private bool isCollected;

        protected virtual void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        protected virtual void Awake()
        {
            if (visual != null)
                visualRestPosition = visual.localPosition;

            // Stagger the bob so several pickups don't move in lockstep.
            bobOffset = UnityEngine.Random.value * Mathf.PI * 2f;
        }

        protected virtual void Update()
        {
            if (visual == null)
                return;

            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            visual.localPosition = visualRestPosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + bobOffset) * bobHeight);
        }

        private void OnTriggerStay(Collider other)
        {
            if (isCollected)
                return;

            PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
            if (player == null || !player.IsAlive || !TryCollect(player.gameObject))
                return;

            isCollected = true;
            Collected?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>
        /// Applies the pickup to the player. Return false to leave it on the ground.
        /// </summary>
        protected abstract bool TryCollect(GameObject player);
    }
}
