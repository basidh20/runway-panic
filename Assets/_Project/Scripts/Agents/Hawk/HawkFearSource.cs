using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// The hawk's fear field (H6): while the hawk herds, it is registered with <see cref="FearRegistry"/>, and pigeons
    /// and crows that read <see cref="FearRegistry.GetRepulsion"/> / <see cref="FearCostModifier"/> are pushed away from
    /// it — towards the player, because the hawk circles on the far side of the flock. Neither side references the
    /// other's class: the registry and <see cref="IFearSource"/> are the only contract (loose coupling).
    /// <para>A plain object owned by <see cref="HawkBrain"/>, not a component, so the hawk prefab needs no extra setup.</para>
    /// </summary>
    public sealed class HawkFearSource : IFearSource
    {
        private readonly Transform owner;
        private readonly HawkStrategyConfig config;

        /// <summary>Creates the field for the hawk at <paramref name="owner"/>; radius and strength come from <paramref name="config"/>.</summary>
        public HawkFearSource(Transform owner, HawkStrategyConfig config)
        {
            this.owner = owner;
            this.config = config;
        }

        /// <summary>True while registered with <see cref="FearRegistry"/>.</summary>
        public bool IsProjecting { get; private set; }

        /// <inheritdoc />
        public Vector3 Position => owner != null ? owner.position : Vector3.zero;

        /// <inheritdoc />
        public float Radius => config.FearRadius;

        /// <inheritdoc />
        public float Strength => config.FearStrength;

        /// <inheritdoc />
        public BirdSpecies SourceSpecies => BirdSpecies.Hawk;

        /// <summary>Registers (true) or removes (false) the field. Safe to call repeatedly.</summary>
        public void SetProjecting(bool projecting)
        {
            if (projecting == IsProjecting)
                return;

            IsProjecting = projecting;
            if (projecting)
                FearRegistry.Add(this);
            else
                FearRegistry.Remove(this);
        }
    }
}
