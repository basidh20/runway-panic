using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// A bird's melee hit on the player (F3, Agent Controller). The species brain decides when to attack and calls
    /// <see cref="TryAttack"/>; this component only checks the species' range and cooldown, plays the strike and
    /// applies the damage through <see cref="PlayerHealth.TakeDamage"/>.
    /// Range is measured from the bird's pivot to the closest point of the player's collider, so a bird at chest
    /// height is in range of a 1.8 m player even though the player's pivot is at the feet.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BirdContactAttack : MonoBehaviour
    {
        private BirdHealth health;
        private BirdAnimatorDriver animatorDriver;
        private IBirdAnimator birdAnimator;
        private float nextAttackTime;

        // The player's health and collider, looked up once per player (not every attack).
        private PlayerHealth cachedPlayer;
        private Collider cachedPlayerCollider;

        /// <summary>True when the cooldown has passed.</summary>
        public bool IsReady => Time.time >= nextAttackTime;

        private BirdSpeciesData Species => health != null ? health.Species : null;

        private void Awake()
        {
            health = GetComponent<BirdHealth>();
            animatorDriver = GetComponentInChildren<BirdAnimatorDriver>();
            birdAnimator = GetComponentInChildren<IBirdAnimator>();
        }

        /// <summary>True when the player is within the species' attack range.</summary>
        public bool IsPlayerInRange()
        {
            PlayerHealth player = ResolvePlayer();
            BirdSpeciesData species = Species;
            if (player == null || species == null)
                return false;

            Vector3 position = transform.position;
            Vector3 closest = cachedPlayerCollider != null && cachedPlayerCollider.enabled
                ? cachedPlayerCollider.bounds.ClosestPoint(position)
                : player.transform.position;
            float range = species.AttackRange;
            return (closest - position).sqrMagnitude <= range * range && HasClearLine(position, closest);
        }

        private static readonly RaycastHit[] LineHits = new RaycastHit[8];

        // A wall, a closed shelter door or a roof between the bird and the player stops the peck: the range test alone
        // let a bird hit a player standing on the other side of a thin wall.
        private bool HasClearLine(Vector3 from, Vector3 to)
        {
            Vector3 line = to - from;
            float distance = line.magnitude;
            if (distance < 0.05f)
                return true;

            int count = Physics.RaycastNonAlloc(from, line / distance, LineHits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = LineHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform) || RunwayPanic.AI.FlightGrid.IsAgentCollider(hit))
                    continue; // this bird, other birds and the player do not block
                return false;
            }

            return true;
        }

        /// <summary>Hits the player with the species' damage if in range and off cooldown. Returns true when it hit.</summary>
        public bool TryAttack()
        {
            BirdSpeciesData species = Species;
            return species != null && TryAttackWithDamage(species.AttackDamage);
        }

        /// <summary>Same as <see cref="TryAttack"/> with a custom damage, for special attacks (stoop, debris).</summary>
        public bool TryAttackWithDamage(float damage)
        {
            BirdSpeciesData species = Species;
            if (species == null || !IsReady || (health != null && !health.IsAlive))
                return false;

            PlayerHealth player = ResolvePlayer();
            // No strike during the respawn grace period either: the bird would play its attack for nothing.
            if (player == null || !player.IsAlive || player.IsInvulnerable || !IsPlayerInRange())
                return false;

            nextAttackTime = Time.time + species.AttackCooldown;

            // The strike (a quick dive) when the Animator-based animator is present; otherwise the generic reaction.
            if (animatorDriver != null)
                animatorDriver.PlayAttack();
            else
                birdAnimator?.PlayHit();

            // Not a weapon hit: the player ignores the rarity (see IDamageable).
            player.TakeDamage(damage, WeaponRarity.Common);
            return true;
        }

        private PlayerHealth ResolvePlayer()
        {
            PlayerEventHub hub = PlayerEventHub.Instance;
            PlayerHealth player = hub != null ? hub.Health : null;
            if (player != cachedPlayer)
            {
                cachedPlayer = player;
                cachedPlayerCollider = player != null ? player.GetComponent<Collider>() : null;
            }
            return player;
        }
    }
}
