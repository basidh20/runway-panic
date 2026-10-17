using System;
using RunwayPanic.Core;
using RunwayPanic.Data;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// A bird's attack. The IS AI decides when to attack and calls <see cref="TryAttack"/>; this component only checks
    /// the species' range and cooldown, plays the attack (dive) animation and applies the damage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BirdAttack : MonoBehaviour
    {
        /// <summary>Raised after a successful attack. Argument: the target that was hit.</summary>
        public event Action<IDamageable> Attacked;

        private BirdAgent agent;
        private BirdHealth health;
        private BirdAnimatorDriver animatorDriver;
        private float nextAttackTime;

        // The last target's transform and collider, so attacking the same target every frame does no lookups.
        private IDamageable cachedTarget;
        private Transform cachedTransform;
        private Collider cachedCollider;

        /// <summary>True when the cooldown has passed.</summary>
        public bool IsReady => Time.time >= nextAttackTime;

        /// <summary>Attack range in metres from the bird's pivot to the target's collider, from the species asset.</summary>
        public float Range => Species != null ? Species.AttackRange : 0f;

        private BirdSpeciesData Species => agent != null ? agent.Species : null;

        private void Awake()
        {
            agent = GetComponent<BirdAgent>();
            health = GetComponent<BirdHealth>();
            animatorDriver = GetComponent<BirdAnimatorDriver>();
        }

        /// <summary>True when <paramref name="target"/> is within the species' attack range.</summary>
        public bool IsInRange(IDamageable target)
        {
            if (!Resolve(target))
                return false;

            Vector3 position = transform.position;
            Vector3 closest = cachedCollider != null && cachedCollider.enabled
                ? cachedCollider.bounds.ClosestPoint(position)
                : cachedTransform.position;
            float range = Range;
            return (closest - position).sqrMagnitude <= range * range;
        }

        /// <summary>
        /// Attacks <paramref name="target"/> if this bird is alive, the cooldown has passed and the target is alive and
        /// in range. Returns true when the attack happened.
        /// </summary>
        public bool TryAttack(IDamageable target)
        {
            BirdSpeciesData species = Species;
            if (species == null || target == null || !target.IsAlive || !IsReady)
                return false;
            if (health != null && !health.IsAlive)
                return false;
            if (!IsInRange(target))
                return false;

            nextAttackTime = Time.time + species.AttackCooldown;
            if (animatorDriver != null)
                animatorDriver.PlayAttack();

            // Not a weapon hit: the receiver ignores the rarity (see IDamageable).
            target.TakeDamage(species.AttackDamage, WeaponRarity.Common);
            Attacked?.Invoke(target);
            return true;
        }

        private bool Resolve(IDamageable target)
        {
            if (target == null)
                return false;

            if (!ReferenceEquals(target, cachedTarget))
            {
                cachedTarget = target;
                var component = target as Component;
                cachedTransform = component != null ? component.transform : null;
                cachedCollider = component != null ? component.GetComponent<Collider>() : null;
            }

            return cachedTransform != null;
        }
    }
}
