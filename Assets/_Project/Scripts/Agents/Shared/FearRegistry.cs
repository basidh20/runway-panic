using System.Collections.Generic;
using RunwayPanic.AI;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Something other birds are afraid of (the hawk while herding). Registered with <see cref="FearRegistry"/>.
    /// Birds of <see cref="SourceSpecies"/> ignore it.
    /// </summary>
    public interface IFearSource
    {
        /// <summary>Centre of the fear field.</summary>
        Vector3 Position { get; }

        /// <summary>Distance at which the fear fades to zero, in metres.</summary>
        float Radius { get; }

        /// <summary>Repulsion at the centre (velocity in m/s; A* cost is this × the modifier's scale).</summary>
        float Strength { get; }

        /// <summary>The species projecting the fear; its own kind is not affected.</summary>
        BirdSpecies SourceSpecies { get; }
    }

    /// <summary>
    /// Fear fields that change other birds' behaviour without anyone referencing anyone's class:
    /// sources register here, and pigeons/crows read <see cref="GetRepulsion"/> in their steering and pass a
    /// <see cref="FearCostModifier"/> to their path searches.
    /// </summary>
    public static class FearRegistry
    {
        private static readonly List<IFearSource> sources = new List<IFearSource>(8);

        /// <summary>The registered sources.</summary>
        public static IReadOnlyList<IFearSource> Sources => sources;

        /// <summary>Adds a source (ignored if already registered).</summary>
        public static void Add(IFearSource source)
        {
            if (source != null && !sources.Contains(source))
                sources.Add(source);
        }

        /// <summary>Removes a source (ignored if not registered).</summary>
        public static void Remove(IFearSource source)
        {
            sources.Remove(source);
        }

        /// <summary>
        /// Velocity pushing a bird of species <paramref name="self"/> at <paramref name="position"/> away from every
        /// other species' fear source in range: Σ direction-away × Strength × (1 − distance / Radius).
        /// Pass the result to <see cref="IBirdMotor.AddSteering"/>.
        /// </summary>
        public static Vector3 GetRepulsion(Vector3 position, BirdSpecies self)
        {
            Vector3 total = Vector3.zero;
            for (int i = 0; i < sources.Count; i++)
            {
                IFearSource source = sources[i];
                if (source.SourceSpecies == self || source.Radius <= 0f)
                    continue;

                Vector3 away = position - source.Position;
                float distance = away.magnitude;
                if (distance >= source.Radius || distance < 0.0001f)
                    continue;

                total += away / distance * (source.Strength * (1f - distance / source.Radius));
            }

            return total;
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            sources.Clear();
        }
    }

    /// <summary>
    /// A* cost from fear: entering a cell costs Σ Strength × <see cref="CostScale"/> × (1 − distance / Radius) over the
    /// fear sources of other species. Create one per bird (or per species) and reuse it for every search.
    /// </summary>
    public sealed class FearCostModifier : ICostModifier
    {
        private readonly BirdSpecies self;

        /// <summary>Creates a modifier for birds of species <paramref name="self"/>.</summary>
        /// <param name="self">Species whose own fear sources are ignored.</param>
        /// <param name="costScale">Path cost per unit of fear strength (10 = a strength-1 source costs like 10 m extra at its centre).</param>
        public FearCostModifier(BirdSpecies self, float costScale = 10f)
        {
            this.self = self;
            CostScale = costScale;
        }

        /// <summary>Path cost per unit of fear strength.</summary>
        public float CostScale { get; }

        /// <inheritdoc />
        public float GetExtraCost(Vector3 worldPosition)
        {
            float cost = 0f;
            IReadOnlyList<IFearSource> sources = FearRegistry.Sources;
            for (int i = 0; i < sources.Count; i++)
            {
                IFearSource source = sources[i];
                if (source.SourceSpecies == self || source.Radius <= 0f)
                    continue;

                float distance = Vector3.Distance(worldPosition, source.Position);
                if (distance < source.Radius)
                    cost += source.Strength * CostScale * (1f - distance / source.Radius);
            }

            return cost;
        }
    }
}
