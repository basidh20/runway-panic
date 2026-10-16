using System;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Player HP. Takes damage from birds and raises events for the HUD and the life system.
    /// Lives and respawning are handled by a separate feature.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] private float maxHP = 100f;

        /// <summary>Raised whenever HP changes. Arguments: current HP, max HP.</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>Raised after damage is applied. Argument: damage taken.</summary>
        public event Action<float> Damaged;

        /// <summary>Raised once when HP reaches zero.</summary>
        public event Action Died;

        public float MaxHP => maxHP;
        public float CurrentHP { get; private set; }
        public bool IsAlive => CurrentHP > 0f;

        private void Awake()
        {
            CurrentHP = maxHP;
        }

        private void Start()
        {
            HealthChanged?.Invoke(CurrentHP, maxHP);
        }

        /// <summary>
        /// Applies damage. The player does not use <paramref name="weaponRarity"/>;
        /// it is part of <see cref="IDamageable"/> for birds.
        /// </summary>
        public void TakeDamage(float amount, WeaponRarity weaponRarity)
        {
            if (!IsAlive || amount <= 0f)
                return;

            float previousHP = CurrentHP;
            CurrentHP = Mathf.Max(0f, CurrentHP - amount);

            Damaged?.Invoke(previousHP - CurrentHP);
            HealthChanged?.Invoke(CurrentHP, maxHP);

            if (!IsAlive)
                Died?.Invoke();
        }

        /// <summary>
        /// Restores full HP. Used when the player respawns.
        /// </summary>
        public void ResetHealth()
        {
            CurrentHP = maxHP;
            HealthChanged?.Invoke(CurrentHP, maxHP);
        }

        private void OnValidate()
        {
            maxHP = Mathf.Max(1f, maxHP);
        }
    }
}
