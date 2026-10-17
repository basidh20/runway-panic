using System;
using RunwayPanic.Core;
using RunwayPanic.Data;
using UnityEngine;

namespace RunwayPanic.Agents
{
    // STUB (F2) so the shared plumbing compiles before F3. The Agent Controller REPLACES THIS FILE in F3
    // (00_SHARED_FOUNDATION.md, phase F3) with the real health: HP from the species asset, damage through
    // species.GetDamageTaken, coins, death. Keep every public member below and the serialized field name
    // 'species' (prefabs already store it); everything else is yours.

    /// <summary>
    /// Bird HP. <b>Stub:</b> registers with <see cref="BirdRegistry"/> and exposes the API brains use, but takes
    /// no damage yet. It implements <see cref="IDamageable"/> so pathfinding already ignores bird colliders.
    /// </summary>
    public sealed class BirdHealth : MonoBehaviour, IDamageable
    {
        [Tooltip("This bird's species asset (ScriptableObjects/Birds). Sets HP, speed, attack and coins.")]
        [SerializeField] private BirdSpeciesData species;

#pragma warning disable CS0067 // Raised by the real implementation (F3); declared now so brains can subscribe.
        /// <summary>Raised when damage is applied. Arguments: damage applied, HP after.</summary>
        public event Action<float, float> Damaged;

        /// <summary>Raised once when the bird dies.</summary>
        public event Action<BirdHealth> Died;
#pragma warning restore CS0067

        /// <summary>The species asset.</summary>
        public BirdSpeciesData Species => species;

        /// <summary>Current HP (stub: always max).</summary>
        public float CurrentHP => species != null ? species.MaxHP : 0f;

        /// <summary>False once dead (stub: always alive).</summary>
        public bool IsAlive => true;

        /// <summary>Muzzle position of the shot that last hit this bird.</summary>
        public Vector3 LastHitShotOrigin { get; private set; }

        /// <summary>Time.time of the last hit (−∞ if never hit).</summary>
        public float LastHitTime { get; private set; } = float.NegativeInfinity;

        /// <inheritdoc />
        public void TakeDamage(float amount, WeaponRarity weaponRarity)
        {
            // Stub: damage arrives in F3.
        }

        private void OnEnable()
        {
            BirdRegistry.Register(this);
        }

        private void OnDisable()
        {
            BirdRegistry.Unregister(this);
        }
    }
}
