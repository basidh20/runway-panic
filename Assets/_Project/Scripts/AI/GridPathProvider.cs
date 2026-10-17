using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// The normal path provider: A* on the scene's <see cref="FlightGrid"/>, then <see cref="PathSmoother"/>.
    /// A start or goal inside a blocked cell (a bird on a roof, a perch on a fin) is snapped to the nearest free cell.
    /// Shows up in the Profiler as <c>RunwayPanic.FindPath</c>.
    /// </summary>
    public sealed class GridPathProvider : IPathProvider
    {
        private static readonly ProfilerMarker FindPathMarker = new ProfilerMarker("RunwayPanic.FindPath");

        private readonly AStarPathfinder pathfinder = new AStarPathfinder();
        private readonly List<Vector3Int> cells = new List<Vector3Int>(256);

        /// <summary>Creates a provider that searches <paramref name="grid"/>.</summary>
        public GridPathProvider(FlightGrid grid)
        {
            Grid = grid != null ? grid : throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>The grid this provider searches.</summary>
        public FlightGrid Grid { get; }

        /// <summary>Cells expanded by the last A* search.</summary>
        public int LastExpandedNodes => pathfinder.LastExpandedNodes;

        /// <summary>True when the last search failed at once because the target is in sealed-off air (e.g. inside a shelter).</summary>
        public bool LastWasUnreachable => pathfinder.LastWasUnreachable;

        /// <inheritdoc />
        public bool TryFindPath(Vector3 from, Vector3 to, IReadOnlyList<ICostModifier> costModifiers, List<Vector3> pathOut)
        {
            if (pathOut == null)
                throw new ArgumentNullException(nameof(pathOut));
            pathOut.Clear();

            using (FindPathMarker.Auto())
            {
                Grid.EnsureBuilt();
                if (!Grid.IsBuilt)
                    return false;

                pathfinder.MaxExpandedNodes = Grid.MaxExpandedNodes;

                // Snapped cells must be in plain sight of the real point, or the first/last leg could cross a wall.
                if (!Grid.TryGetNearestFree(Grid.ClampToGrid(from), Grid.SnapRadius, out Vector3Int start, from) ||
                    !Grid.TryGetNearestFree(Grid.ClampToGrid(to), Grid.SnapRadius, out Vector3Int goal, to))
                    return false;

                if (!pathfinder.FindPath(Grid, start, goal, costModifiers, cells))
                    return false;

                // [from, cell centres..., to]: the smoother then cuts straight across wherever it can.
                pathOut.Add(from);
                for (int i = 0; i < cells.Count; i++)
                    pathOut.Add(Grid.CellToWorld(cells[i]));

                Vector3 lastCell = pathOut[pathOut.Count - 1];
                if (Grid.IsFlyable(to) && PathSmoother.IsClear(lastCell, to, Grid.SnapLineRadius, Grid.ObstacleMask))
                    pathOut.Add(to);

                PathSmoother.Smooth(pathOut, Grid.Clearance, Grid.ObstacleMask);

                // The bird is already at 'from'.
                pathOut.RemoveAt(0);
                return true;
            }
        }

        /// <inheritdoc />
        public bool IsFlyable(Vector3 worldPosition)
        {
            return Grid.IsFlyable(worldPosition);
        }
    }
}
