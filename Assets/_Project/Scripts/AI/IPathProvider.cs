using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// Finds flight paths through the level. Brains get the current one from <see cref="PathService.Current"/>,
    /// so they work the same with the A* grid or the straight-line fallback.
    /// </summary>
    public interface IPathProvider
    {
        /// <summary>
        /// Clears <paramref name="pathOut"/> and fills it with world-space waypoints from <paramref name="from"/>
        /// to <paramref name="to"/>. The start point is not included; the last point is <paramref name="to"/>
        /// when it can be flown to. <paramref name="costModifiers"/> may be null.
        /// Returns false when no path was found.
        /// </summary>
        bool TryFindPath(Vector3 from, Vector3 to, IReadOnlyList<ICostModifier> costModifiers, List<Vector3> pathOut);

        /// <summary>True when a bird can occupy <paramref name="worldPosition"/> without touching level geometry.</summary>
        bool IsFlyable(Vector3 worldPosition);
    }
}
