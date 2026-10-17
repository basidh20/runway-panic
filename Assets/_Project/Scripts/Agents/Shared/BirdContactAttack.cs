using UnityEngine;

namespace RunwayPanic.Agents
{
    // STUB (F2) so brains compile before F3. The Agent Controller REPLACES THIS FILE in F3
    // (00_SHARED_FOUNDATION.md, phase F3): range check against species.AttackRange, cooldown from
    // species.AttackCooldown, damage through PlayerHealth.TakeDamage. Keep the three public methods below.

    /// <summary>
    /// A bird's melee hit on the player. <b>Stub:</b> never in range, never attacks.
    /// </summary>
    public sealed class BirdContactAttack : MonoBehaviour
    {
        /// <summary>True when the player is within the species' attack range (stub: false).</summary>
        public bool IsPlayerInRange()
        {
            return false;
        }

        /// <summary>Hits the player with the species' damage if in range and off cooldown (stub: false).</summary>
        public bool TryAttack()
        {
            return false;
        }

        /// <summary>Same as <see cref="TryAttack"/> with a custom damage, for special attacks (stoop, debris). Stub: false.</summary>
        public bool TryAttackWithDamage(float damage)
        {
            return false;
        }
    }
}
