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

        /// <summary>Raised when a hit is fully absorbed (shield up). No HP is lost and <see cref="Damaged"/> is not raised.</summary>
        public event Action Blocked;

        public float MaxHP => maxHP;
        public float CurrentHP { get; private set; }
        public bool IsAlive => CurrentHP > 0f;

        /// <summary>True during the short grace period after a respawn: all damage is ignored.</summary>
        public bool IsInvulnerable => Time.time < invulnerableUntil;

        private PlayerShield shield;
        private float invulnerableUntil = float.NegativeInfinity;

        /// <summary>Ignores all damage for <paramref name="seconds"/> (respawn grace period).</summary>
        public void SetInvulnerable(float seconds)
        {
            invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + Mathf.Max(0f, seconds));
        }

        private void Awake()
        {
            CurrentHP = maxHP;
            shield = GetComponent<PlayerShield>();
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
            if (!IsAlive || amount <= 0f || IsInvulnerable)
                return;

            if (shield != null)
                amount *= shield.DamageMultiplier;

            // Fully blocked (an active shield): no flash, no hurt sound, no hit reaction.
            if (amount <= 0f)
            {
                Blocked?.Invoke();
                return;
            }

            float previousHP = CurrentHP;
            CurrentHP = Mathf.Max(0f, CurrentHP - amount);

            Damaged?.Invoke(previousHP - CurrentHP);
            HealthChanged?.Invoke(CurrentHP, maxHP);

            if (!IsAlive)
                Died?.Invoke();
        }

        /// <summary>
        /// Restores up to <paramref name="amount"/> HP without passing the maximum. Returns the HP actually restored.
        /// </summary>
        public float Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
                return 0f;

            float previousHP = CurrentHP;
            CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
            float healed = CurrentHP - previousHP;

            if (healed > 0f)
                HealthChanged?.Invoke(CurrentHP, maxHP);

            return healed;
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
