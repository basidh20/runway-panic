using System;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Temporary damage immunity from a shield pickup. While active, <see cref="PlayerHealth"/> multiplies
    /// incoming damage by <see cref="DamageMultiplier"/> and a bubble is shown around the player.
    /// Dying removes the shield.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerShield : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float duration = 10f;
        [Tooltip("Fraction of damage blocked while active (1 = all damage).")]
        [SerializeField, Range(0f, 1f)] private float damageReduction = 1f;
        [Tooltip("Shown while the shield is active.")]
        [SerializeField] private GameObject bubble;

        /// <summary>Raised when a shield starts. Argument: duration in seconds.</summary>
        public event Action<float> Activated;

        /// <summary>Raised when the shield runs out or is removed.</summary>
        public event Action Expired;

        private PlayerHealth health;
        private float endTime;

        public bool IsActive { get; private set; }
        public float RemainingTime => IsActive ? Mathf.Max(0f, endTime - Time.time) : 0f;
        public float DamageMultiplier => IsActive ? 1f - damageReduction : 1f;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            if (bubble != null)
                bubble.SetActive(false);
        }

        private void OnEnable()
        {
            health.Died += Deactivate;
        }

        private void OnDisable()
        {
            health.Died -= Deactivate;
        }

        private void Update()
        {
            if (IsActive && Time.time >= endTime)
                Deactivate();
        }

        /// <summary>
        /// Starts a shield. Returns false if one is already running, so a pickup is not wasted.
        /// </summary>
        public bool TryActivate()
        {
            if (IsActive || !health.IsAlive)
                return false;

            IsActive = true;
            endTime = Time.time + duration;
            if (bubble != null)
                bubble.SetActive(true);

            Activated?.Invoke(duration);
            return true;
        }

        public void Deactivate()
        {
            if (!IsActive)
                return;

            IsActive = false;
            if (bubble != null)
                bubble.SetActive(false);

            Expired?.Invoke();
        }
    }
}
