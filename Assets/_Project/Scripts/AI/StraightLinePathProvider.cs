using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// Fallback used when the scene has no <see cref="FlightGrid"/> (early sandboxes): the path is just the target.
    /// Birds fly straight at it and can clip through buildings, so this is for testing only.
    /// </summary>
    public sealed class StraightLinePathProvider : IPathProvider
    {
        // Matches the grid's default flyable radius, so both providers agree on what "flyable" means.
        private const float FlyableRadius = 0.3f;

        /// <inheritdoc />
        public bool TryFindPath(Vector3 from, Vector3 to, IReadOnlyList<ICostModifier> costModifiers, List<Vector3> pathOut)
        {
            pathOut.Clear();
            pathOut.Add(to);
            return true;
        }

        /// <inheritdoc />
        public bool IsFlyable(Vector3 worldPosition)
        {
            return FlightGrid.IsSpaceFree(worldPosition, FlyableRadius, Physics.AllLayers);
        }
    }
}
