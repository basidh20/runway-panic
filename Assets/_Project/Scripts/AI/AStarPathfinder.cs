using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// A* over a <see cref="FlightGrid"/> with 26-neighbour moves (straight, edge-diagonal and corner-diagonal).
    /// Step cost = move length × cell size + the extra cost of the entered cell from every <see cref="ICostModifier"/>.
    /// The heuristic is the 3D octile distance, which never overestimates (modifier costs are ≥ 0).
    /// It is scaled by <see cref="TieBreak"/> so that, among the many equally cheap routes through open air,
    /// the search keeps pushing toward the goal instead of exploring all of them; paths are at most 0.1 % longer.
    /// Goals in a different free-air region than the start (<see cref="FlightGrid.AreConnected"/>) fail at once.
    /// One instance is reused for every search: its arrays are sized to the grid once and reset with a
    /// search-id stamp instead of being cleared, so a search allocates nothing.
    /// </summary>
    public sealed class AStarPathfinder
    {
        /// <summary>Heuristic weight. Just above 1 breaks ties toward the goal; the path cost stays within 0.1 % of optimal.</summary>
        public const float TieBreak = 1.001f;

        private const float Sqrt2 = 1.41421356f;
        private const float Sqrt3 = 1.73205081f;

        private static readonly Vector3Int[] Offsets = BuildOffsets();
        private static readonly float[] StepLengths = BuildStepLengths(Offsets);

        private readonly MinHeap<int> open = new MinHeap<int>(1024);

        // Per-cell search data. A value is valid only when its stamp equals the current search id.
        private float[] gScore;
        private int[] parent;
        private int[] seenStamp;
        private int[] closedStamp;
        private float[] extraCost;
        private int[] extraCostStamp;
        private int searchId;

        // Flat-index offsets for the current grid size: the neighbour itself, and the straight/edge cells a
        // diagonal move passes (all must be free, so a move never cuts a blocked corner).
        private int deltaSizeX = -1, deltaSizeY = -1;
        private readonly int[] neighbourDeltas = new int[26];
        private readonly int[][] cornerDeltas = new int[26][];

        /// <summary>The search gives up (returns false) after expanding this many cells.</summary>
        public int MaxExpandedNodes { get; set; } = 8000;

        /// <summary>Cells expanded by the last search, for profiling and the viva.</summary>
        public int LastExpandedNodes { get; private set; }

        /// <summary>True when the last search failed at once because start and goal are in separate regions.</summary>
        public bool LastWasUnreachable { get; private set; }

        /// <summary>
        /// Finds a path from <paramref name="start"/> to <paramref name="goal"/>. Both must be free cells.
        /// Clears <paramref name="cellsOut"/> and fills it with the cells from start to goal, both included.
        /// <paramref name="costModifiers"/> may be null.
        /// </summary>
        public bool FindPath(FlightGrid grid, Vector3Int start, Vector3Int goal, IReadOnlyList<ICostModifier> costModifiers, List<Vector3Int> cellsOut)
        {
            cellsOut.Clear();
            LastExpandedNodes = 0;
            LastWasUnreachable = false;

            if (grid == null || !grid.IsBuilt || grid.IsBlocked(start) || grid.IsBlocked(goal))
                return false;

            int startIndex = grid.ToIndex(start);
            int goalIndex = grid.ToIndex(goal);
            if (!grid.AreConnected(startIndex, goalIndex))
            {
                LastWasUnreachable = true;
                return false;
            }

            EnsureCapacity(grid);
            NextSearchId();

            Vector3Int size = grid.Size;
            float cellSize = grid.CellSize;
            float heuristicScale = cellSize * TieBreak;

            open.Clear();
            seenStamp[startIndex] = searchId;
            gScore[startIndex] = 0f;
            parent[startIndex] = -1;
            open.Push(startIndex, Heuristic(start.x, start.y, start.z, goal) * heuristicScale);

            while (open.Count > 0)
            {
                int current = open.Pop();

                // The heap may hold older, worse copies of a cell that was improved later: skip them.
                if (closedStamp[current] == searchId)
                    continue;
                closedStamp[current] = searchId;

                if (current == goalIndex)
                {
                    Reconstruct(grid, goalIndex, cellsOut);
                    return true;
                }

                if (++LastExpandedNodes > MaxExpandedNodes)
                    return false;

                Vector3Int cell = grid.IndexToCell(current);
                float currentG = gScore[current];

                for (int n = 0; n < 26; n++)
                {
                    Vector3Int offset = Offsets[n];
                    int nx = cell.x + offset.x, ny = cell.y + offset.y, nz = cell.z + offset.z;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= size.x || ny >= size.y || nz >= size.z)
                        continue;

                    int nextIndex = current + neighbourDeltas[n];
                    if (closedStamp[nextIndex] == searchId || grid.IsBlockedIndex(nextIndex))
                        continue;

                    // The passed cells lie between the current cell and an in-bounds neighbour, so they are in bounds too.
                    int[] corners = cornerDeltas[n];
                    bool cutsCorner = false;
                    for (int c = 0; c < corners.Length; c++)
                    {
                        if (grid.IsBlockedIndex(current + corners[c]))
                        {
                            cutsCorner = true;
                            break;
                        }
                    }

                    if (cutsCorner)
                        continue;

                    // Modifier costs are ≥ 0, so if the bare step is already no better, the full step can't be.
                    float g = currentG + StepLengths[n] * cellSize;
                    if (seenStamp[nextIndex] == searchId && g >= gScore[nextIndex])
                        continue;

                    if (costModifiers != null && costModifiers.Count > 0)
                    {
                        g += GetExtraCost(grid, nextIndex, costModifiers);
                        if (seenStamp[nextIndex] == searchId && g >= gScore[nextIndex])
                            continue;
                    }

                    seenStamp[nextIndex] = searchId;
                    gScore[nextIndex] = g;
                    parent[nextIndex] = current;
                    open.Push(nextIndex, g + Heuristic(nx, ny, nz, goal) * heuristicScale);
                }
            }

            return false;
        }

        /// <summary>
        /// 3D octile distance in cells: with sorted axis distances a ≥ b ≥ c, the cheapest obstacle-free route is
        /// c corner-diagonal steps (√3), then b − c edge-diagonal steps (√2), then a − b straight steps.
        /// </summary>
        private static float Heuristic(int x, int y, int z, Vector3Int to)
        {
            int a = Mathf.Abs(to.x - x);
            int b = Mathf.Abs(to.y - y);
            int c = Mathf.Abs(to.z - z);

            // Sort so that a ≥ b ≥ c.
            if (a < b) (a, b) = (b, a);
            if (b < c) (b, c) = (c, b);
            if (a < b) (a, b) = (b, a);

            return Sqrt3 * c + Sqrt2 * (b - c) + (a - b);
        }

        // The extra cost of a cell is the same whichever neighbour enters it, so it is computed once per search.
        private float GetExtraCost(FlightGrid grid, int index, IReadOnlyList<ICostModifier> costModifiers)
        {
            if (extraCostStamp[index] == searchId)
                return extraCost[index];

            Vector3 world = grid.CellToWorld(grid.IndexToCell(index));
            float total = 0f;
            for (int i = 0; i < costModifiers.Count; i++)
                if (costModifiers[i] != null)
                    total += Mathf.Max(0f, costModifiers[i].GetExtraCost(world));

            extraCostStamp[index] = searchId;
            extraCost[index] = total;
            return total;
        }

        private void Reconstruct(FlightGrid grid, int goalIndex, List<Vector3Int> cellsOut)
        {
            for (int index = goalIndex; index != -1; index = parent[index])
                cellsOut.Add(grid.IndexToCell(index));
            cellsOut.Reverse();
        }

        private void EnsureCapacity(FlightGrid grid)
        {
            int cellCount = grid.CellCount;
            if (gScore == null || gScore.Length != cellCount)
            {
                gScore = new float[cellCount];
                parent = new int[cellCount];
                seenStamp = new int[cellCount];
                closedStamp = new int[cellCount];
                extraCost = new float[cellCount];
                extraCostStamp = new int[cellCount];
                searchId = 0;
            }

            Vector3Int size = grid.Size;
            if (size.x != deltaSizeX || size.y != deltaSizeY)
            {
                deltaSizeX = size.x;
                deltaSizeY = size.y;
                BuildDeltas(size.x, size.x * size.y);
            }
        }

        private void BuildDeltas(int strideY, int strideZ)
        {
            var corners = new List<int>(6);
            for (int n = 0; n < 26; n++)
            {
                Vector3Int o = Offsets[n];
                neighbourDeltas[n] = o.x + o.y * strideY + o.z * strideZ;

                corners.Clear();
                int axes = (o.x != 0 ? 1 : 0) + (o.y != 0 ? 1 : 0) + (o.z != 0 ? 1 : 0);
                if (axes >= 2)
                {
                    if (o.x != 0) corners.Add(o.x);
                    if (o.y != 0) corners.Add(o.y * strideY);
                    if (o.z != 0) corners.Add(o.z * strideZ);
                }

                if (axes == 3)
                {
                    corners.Add(o.x + o.y * strideY);
                    corners.Add(o.x + o.z * strideZ);
                    corners.Add(o.y * strideY + o.z * strideZ);
                }

                cornerDeltas[n] = corners.ToArray();
            }
        }

        private void NextSearchId()
        {
            if (searchId == int.MaxValue)
            {
                // After ~2 billion searches the stamps would repeat: start over from clean arrays.
                System.Array.Clear(seenStamp, 0, seenStamp.Length);
                System.Array.Clear(closedStamp, 0, closedStamp.Length);
                System.Array.Clear(extraCostStamp, 0, extraCostStamp.Length);
                searchId = 0;
            }

            searchId++;
        }

        private static Vector3Int[] BuildOffsets()
        {
            var offsets = new Vector3Int[26];
            int i = 0;
            for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                        if (x != 0 || y != 0 || z != 0)
                            offsets[i++] = new Vector3Int(x, y, z);
            return offsets;
        }

        private static float[] BuildStepLengths(Vector3Int[] offsets)
        {
            var lengths = new float[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
                lengths[i] = Mathf.Sqrt(offsets[i].x * offsets[i].x + offsets[i].y * offsets[i].y + offsets[i].z * offsets[i].z);
            return lengths;
        }
    }
}
