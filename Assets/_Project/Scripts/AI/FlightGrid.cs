using System;
using RunwayPanic.Core;
using RunwayPanic.Interaction;
using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// 3D voxel grid over the level that A* searches for flying birds (a ground NavMesh can't describe air).
    /// Every cell is tested once against level colliders: a cell is blocked when a box of
    /// <c>cellSize / 2 + clearance</c> around its centre touches anything except birds and the player.
    /// Moved barricades and gull debris update only the cells they touch.
    /// One per scene, reached through <see cref="Instance"/>; it also holds the scene's path-search settings.
    /// </summary>
    public sealed class FlightGrid : MonoBehaviour
    {
        // Safety cap so a typo in the bounds (e.g. 0.05 m cells) can't allocate gigabytes.
        private const int MaxCells = 2_000_000;
        private const int OverlapBufferSize = 32;

        // Shared by every query: physics queries run on the main thread only, one at a time.
        private static readonly Collider[] OverlapBuffer = new Collider[OverlapBufferSize];

        [Header("Volume")]
        [Tooltip("World-space centre of the flyable volume. Default fits the 100 × 80 m airport from 0.5 m to 24.5 m: " +
                 "the 0.5 m floor keeps the lowest cell layer off the ground (y = 0), so low cover and barricades still register.")]
        [SerializeField] private Vector3 boundsCenter = new Vector3(0f, 12.5f, 0f);
        [Tooltip("World-space size of the flyable volume, in metres.")]
        [SerializeField] private Vector3 boundsSize = new Vector3(100f, 24f, 80f);
        [Tooltip("Edge length of one cell, in metres. Smaller = more precise paths, more memory and slower searches.")]
        [SerializeField, Min(0.5f)] private float cellSize = 2f;

        [Header("Obstacles")]
        [Tooltip("Layers that block flight. Birds and the player are always ignored (anything with an IDamageable).")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        [Tooltip("Extra margin around each cell when testing for obstacles, in metres. Keeps paths away from walls.")]
        [SerializeField, Min(0f)] private float clearance = 0.4f;
        [Tooltip("Radius of the free-space test for a single point (IsFlyable), in metres.")]
        [SerializeField, Min(0.05f)] private float flyableRadius = 0.3f;

        [Header("Search")]
        [Tooltip("A* gives up after expanding this many cells, so one bad request can't freeze a frame.")]
        [SerializeField, Min(100)] private int maxExpandedNodes = 8000;
        [Tooltip("A* searches allowed per frame across all birds. Brains that are refused retry next frame.")]
        [SerializeField, Min(1)] private int maxSearchesPerFrame = 6;
        [Tooltip("When a path's start or goal cell is blocked, search this many cells around it for a free one.")]
        [SerializeField, Min(0)] private int snapRadius = 3;
        [Tooltip("Radius of the line test that makes sure a snapped start/goal cell is not behind a wall, in metres.")]
        [SerializeField, Min(0.01f)] private float snapLineRadius = 0.1f;

        [Header("Debug")]
        [Tooltip("Draw cells while this object is selected (Play mode, or after 'Build Preview').")]
        [SerializeField] private bool drawGizmos = true;
        [Tooltip("Draw only blocked cells. Off also draws free cells faintly (slow on big grids).")]
        [SerializeField] private bool gizmoOnlyBlocked = true;
        [Tooltip("Log the cell count, blocked count and build time when the grid is built.")]
        [SerializeField] private bool logBuildTime = true;

        private Vector3 origin;
        private int sizeX, sizeY, sizeZ;
        private bool[] geometryBlocked;
        private int[] manualBlocks;

        // Connected regions of free cells (0 = blocked), so unreachable goals fail instantly instead of
        // searching thousands of cells. Relabelled lazily after any cell changes.
        private int[] regionIds;
        private int[] floodQueue;
        private bool regionsDirty = true;
        private int regionCount;

        private Barricade[] barricades;
        private Bounds[] barricadeFootprints;
        private Action<Bounds>[] barricadeHandlers;

        public static FlightGrid Instance { get; private set; }

        /// <summary>True once the cells have been tested against the level.</summary>
        public bool IsBuilt { get; private set; }

        public float CellSize => cellSize;
        public float Clearance => clearance;
        public LayerMask ObstacleMask => obstacleMask;
        public int MaxExpandedNodes => maxExpandedNodes;
        public int MaxSearchesPerFrame => maxSearchesPerFrame;
        public int SnapRadius => snapRadius;
        public float SnapLineRadius => snapLineRadius;
        public float FlyableRadius => flyableRadius;
        public Vector3Int Size => new Vector3Int(sizeX, sizeY, sizeZ);

        /// <summary>The configured flyable volume in world space (the built cells may extend slightly past it).</summary>
        public Bounds VolumeBounds => new Bounds(boundsCenter, boundsSize);
        public int CellCount => sizeX * sizeY * sizeZ;

        /// <summary>Number of cells blocked by level geometry after the last build.</summary>
        public int BlockedCellCount { get; private set; }

        /// <summary>
        /// Number of separate regions of free air. 1 means every free cell can reach every other; more means some air
        /// is sealed off (e.g. inside a shelter whose doorway is narrower than a cell plus clearance).
        /// </summary>
        public int RegionCount
        {
            get
            {
                RefreshRegions();
                return regionCount;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"A second {nameof(FlightGrid)} on '{name}' was disabled; '{Instance.name}' is already active.", this);
                enabled = false;
                return;
            }

            Instance = this;
            // An edit-mode preview may survive into Play mode when domain reload is off; always rebuild.
            IsBuilt = false;
        }

        private void Start()
        {
            if (Instance != this)
                return;

            // Built here, not in Awake: by Start every collider in the scene is registered with physics.
            EnsureBuilt();
            SubscribeToBarricades();
        }

        private void OnDestroy()
        {
            UnsubscribeFromBarricades();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Builds the grid if it hasn't been built yet. Cheap to call before every search.</summary>
        public void EnsureBuilt()
        {
            if (!IsBuilt)
                Build();
        }

        /// <summary>Tests every cell against the level colliders. Takes tens of milliseconds; call once at load.</summary>
        public void Build()
        {
            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();

            sizeX = Mathf.Max(1, Mathf.CeilToInt(boundsSize.x / cellSize));
            sizeY = Mathf.Max(1, Mathf.CeilToInt(boundsSize.y / cellSize));
            sizeZ = Mathf.Max(1, Mathf.CeilToInt(boundsSize.z / cellSize));
            origin = boundsCenter - boundsSize * 0.5f;

            long cellCount = (long)sizeX * sizeY * sizeZ;
            if (cellCount > MaxCells)
            {
                Debug.LogError($"{nameof(FlightGrid)} would need {cellCount:N0} cells (limit {MaxCells:N0}). Increase the cell size or shrink the bounds.", this);
                sizeX = sizeY = sizeZ = 0;
                geometryBlocked = null;
                manualBlocks = null;
                IsBuilt = false;
                return;
            }

            int count = (int)cellCount;
            if (geometryBlocked == null || geometryBlocked.Length != count)
            {
                geometryBlocked = new bool[count];
                manualBlocks = new int[count];
            }

            Physics.SyncTransforms();

            int blocked = 0;
            for (int i = 0; i < count; i++)
            {
                geometryBlocked[i] = TestCell(CellToWorld(IndexToCell(i)));
                if (geometryBlocked[i])
                    blocked++;
            }

            BlockedCellCount = blocked;
            IsBuilt = true;
            regionsDirty = true;
            RefreshRegions();

            if (logBuildTime)
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Debug.Log($"[FlightGrid] {sizeX} × {sizeY} × {sizeZ} = {count:N0} cells of {cellSize} m, {blocked:N0} blocked, " +
                          $"{regionCount} free-air region(s), built in {ms:F1} ms.", this);
            }
        }

        /// <summary>
        /// True when A* can get from one free cell to the other. Both are flat indices of free cells.
        /// Cost modifiers never block, so this is exact. O(1) after a relabel.
        /// </summary>
        public bool AreConnected(int fromIndex, int toIndex)
        {
            RefreshRegions();
            int region = regionIds[fromIndex];
            return region != 0 && region == regionIds[toIndex];
        }

        /// <summary>Re-tests only the cells that overlap <paramref name="worldBounds"/> (e.g. after a barricade moved).</summary>
        public void RebuildRegion(Bounds worldBounds)
        {
            if (!IsBuilt)
                return;

            Physics.SyncTransforms();
            GetCellRange(worldBounds, out Vector3Int min, out Vector3Int max);
            for (int z = min.z; z <= max.z; z++)
                for (int y = min.y; y <= max.y; y++)
                    for (int x = min.x; x <= max.x; x++)
                    {
                        int index = ToIndex(x, y, z);
                        bool wasBlocked = geometryBlocked[index];
                        bool isBlocked = TestCell(CellToWorld(new Vector3Int(x, y, z)));
                        geometryBlocked[index] = isBlocked;
                        if (wasBlocked != isBlocked)
                        {
                            BlockedCellCount += isBlocked ? 1 : -1;
                            regionsDirty = true;
                        }
                    }
        }

        /// <summary>
        /// Blocks (or releases) every cell overlapping <paramref name="worldBounds"/>, independent of geometry,
        /// e.g. for gull debris. Blocks stack: each block must later be released with the same bounds.
        /// <see cref="RebuildRegion"/> never clears them.
        /// </summary>
        public void SetManualBlock(Bounds worldBounds, bool blocked)
        {
            EnsureBuilt();
            if (!IsBuilt)
                return;

            GetCellRange(worldBounds, out Vector3Int min, out Vector3Int max);
            for (int z = min.z; z <= max.z; z++)
                for (int y = min.y; y <= max.y; y++)
                    for (int x = min.x; x <= max.x; x++)
                    {
                        int index = ToIndex(x, y, z);
                        manualBlocks[index] = blocked ? manualBlocks[index] + 1 : Mathf.Max(0, manualBlocks[index] - 1);
                    }

            regionsDirty = true;
        }

        /// <summary>Cell containing <paramref name="worldPosition"/>. Returns false when it lies outside the grid.</summary>
        public bool WorldToCell(Vector3 worldPosition, out Vector3Int cell)
        {
            Vector3 local = (worldPosition - origin) / cellSize;
            cell = new Vector3Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y), Mathf.FloorToInt(local.z));
            return InBounds(cell);
        }

        /// <summary>Cell containing <paramref name="worldPosition"/>, clamped to the grid's edge when outside it.</summary>
        public Vector3Int ClampToGrid(Vector3 worldPosition)
        {
            WorldToCell(worldPosition, out Vector3Int cell);
            return new Vector3Int(
                Mathf.Clamp(cell.x, 0, sizeX - 1),
                Mathf.Clamp(cell.y, 0, sizeY - 1),
                Mathf.Clamp(cell.z, 0, sizeZ - 1));
        }

        /// <summary>World-space centre of <paramref name="cell"/>.</summary>
        public Vector3 CellToWorld(Vector3Int cell)
        {
            return origin + new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f) * cellSize;
        }

        /// <summary>True when the cell can't be flown through. Cells outside the grid count as blocked.</summary>
        public bool IsBlocked(Vector3Int cell)
        {
            return !InBounds(cell) || IsBlockedIndex(ToIndex(cell.x, cell.y, cell.z));
        }

        /// <summary>Same as <see cref="IsBlocked"/> for a flat index from <see cref="ToIndex(Vector3Int)"/>. No bounds check.</summary>
        public bool IsBlockedIndex(int index)
        {
            return geometryBlocked[index] || manualBlocks[index] > 0;
        }

        /// <summary>True when <paramref name="cell"/> lies inside the built grid.</summary>
        public bool InBounds(Vector3Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.z >= 0 && cell.x < sizeX && cell.y < sizeY && cell.z < sizeZ;
        }

        /// <summary>Flat array index of an in-bounds cell: <c>x + y·sizeX + z·sizeX·sizeY</c>.</summary>
        public int ToIndex(Vector3Int cell)
        {
            return ToIndex(cell.x, cell.y, cell.z);
        }

        /// <summary>Inverse of <see cref="ToIndex(Vector3Int)"/>.</summary>
        public Vector3Int IndexToCell(int index)
        {
            int layer = sizeX * sizeY;
            int z = index / layer;
            int rest = index - z * layer;
            int y = rest / sizeX;
            return new Vector3Int(rest - y * sizeX, y, z);
        }

        /// <summary>
        /// Nearest free cell to <paramref name="cell"/>, searching cubic shells up to <paramref name="maxRadius"/> cells out.
        /// Returns the cell itself when it is free. Within a shell, the cell closest in straight-line distance wins.
        /// With <paramref name="visibleFrom"/>, a cell only counts when a clear line runs from that point to its centre,
        /// so a bird next to a wall is never snapped to the far side of it (e.g. the air inside a hangar below its roof).
        /// </summary>
        public bool TryGetNearestFree(Vector3Int cell, int maxRadius, out Vector3Int free, Vector3? visibleFrom = null)
        {
            free = cell;
            if (!IsBuilt)
                return false;
            if (!IsBlocked(cell))
                return true;

            for (int r = 1; r <= maxRadius; r++)
            {
                int bestSqr = int.MaxValue;
                for (int dz = -r; dz <= r; dz++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            // Only the shell at exactly r; inner shells were already checked.
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz))) != r)
                                continue;

                            var candidate = new Vector3Int(cell.x + dx, cell.y + dy, cell.z + dz);
                            int sqr = dx * dx + dy * dy + dz * dz;
                            if (sqr < bestSqr && !IsBlocked(candidate) &&
                                (!visibleFrom.HasValue || PathSmoother.IsClear(visibleFrom.Value, CellToWorld(candidate), snapLineRadius, obstacleMask)))
                            {
                                bestSqr = sqr;
                                free = candidate;
                            }
                        }

                if (bestSqr != int.MaxValue)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True when a bird can be at <paramref name="worldPosition"/>: inside the grid, not in a manually blocked cell,
        /// and no level collider within the flyable radius. Finer than <see cref="IsBlocked"/>, which works per cell.
        /// </summary>
        public bool IsFlyable(Vector3 worldPosition)
        {
            EnsureBuilt();
            if (!IsBuilt || !WorldToCell(worldPosition, out Vector3Int cell))
                return false;
            if (manualBlocks[ToIndex(cell)] > 0)
                return false;
            return IsSpaceFree(worldPosition, flyableRadius, obstacleMask);
        }

        /// <summary>True when no collider other than a bird or the player overlaps the sphere.</summary>
        public static bool IsSpaceFree(Vector3 position, float radius, LayerMask mask)
        {
            int count = Physics.OverlapSphereNonAlloc(position, radius, OverlapBuffer, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!IsAgentCollider(OverlapBuffer[i]))
                    return false;
            // A full buffer may hide a level collider behind agent colliders: treat it as blocked.
            return count < OverlapBufferSize;
        }

        /// <summary>True for colliders of birds, the player and anything else that has HP; they never block paths.</summary>
        public static bool IsAgentCollider(Collider collider)
        {
            return collider.GetComponentInParent<IDamageable>() != null;
        }

        private bool TestCell(Vector3 center)
        {
            Vector3 halfExtents = Vector3.one * (cellSize * 0.5f + clearance);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, OverlapBuffer, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!IsAgentCollider(OverlapBuffer[i]))
                    return true;
            return count == OverlapBufferSize;
        }

        private int ToIndex(int x, int y, int z)
        {
            return x + y * sizeX + z * sizeX * sizeY;
        }

        // Flood fill over free cells with face neighbours only. That matches A* exactly: a diagonal move is only
        // allowed when its straight components are free, so every diagonal step has a face-connected detour.
        private void RefreshRegions()
        {
            if (!regionsDirty || !IsBuilt)
                return;

            int count = CellCount;
            if (regionIds == null || regionIds.Length != count)
            {
                regionIds = new int[count];
                floodQueue = new int[count];
            }

            System.Array.Clear(regionIds, 0, count);
            int layer = sizeX * sizeY;
            regionCount = 0;

            for (int seed = 0; seed < count; seed++)
            {
                if (regionIds[seed] != 0 || IsBlockedIndex(seed))
                    continue;

                int region = ++regionCount;
                int head = 0, tail = 0;
                regionIds[seed] = region;
                floodQueue[tail++] = seed;

                while (head < tail)
                {
                    int index = floodQueue[head++];
                    int z = index / layer;
                    int rest = index - z * layer;
                    int y = rest / sizeX;
                    int x = rest - y * sizeX;

                    if (x > 0) Visit(index - 1, region, ref tail);
                    if (x < sizeX - 1) Visit(index + 1, region, ref tail);
                    if (y > 0) Visit(index - sizeX, region, ref tail);
                    if (y < sizeY - 1) Visit(index + sizeX, region, ref tail);
                    if (z > 0) Visit(index - layer, region, ref tail);
                    if (z < sizeZ - 1) Visit(index + layer, region, ref tail);
                }
            }

            regionsDirty = false;
        }

        private void Visit(int index, int region, ref int tail)
        {
            if (regionIds[index] != 0 || IsBlockedIndex(index))
                return;
            regionIds[index] = region;
            floodQueue[tail++] = index;
        }

        private void GetCellRange(Bounds worldBounds, out Vector3Int min, out Vector3Int max)
        {
            min = ClampToGrid(worldBounds.min);
            max = ClampToGrid(worldBounds.max);
        }

        private void SubscribeToBarricades()
        {
            barricades = FindObjectsByType<Barricade>();
            barricadeFootprints = new Bounds[barricades.Length];
            barricadeHandlers = new Action<Bounds>[barricades.Length];

            for (int i = 0; i < barricades.Length; i++)
            {
                int index = i;
                barricadeFootprints[i] = barricades[i].Footprint;
                barricadeHandlers[i] = footprint => HandleBarricadeSettled(index, footprint);
                barricades[i].Settled += barricadeHandlers[i];
            }
        }

        private void UnsubscribeFromBarricades()
        {
            if (barricades == null)
                return;

            for (int i = 0; i < barricades.Length; i++)
                if (barricades[i] != null)
                    barricades[i].Settled -= barricadeHandlers[i];

            barricades = null;
        }

        /// <summary>
        /// Updates the grid after an obstacle moved: re-tests where it was (to free those cells) and where it is now
        /// (to block them). Pass the obstacle's collider bounds; one cell of margin is added for the clearance.
        /// </summary>
        public void RefreshMovedObstacle(Bounds previousBounds, Bounds currentBounds)
        {
            previousBounds.Expand(cellSize * 2f);
            currentBounds.Expand(cellSize * 2f);
            RebuildRegion(previousBounds);
            RebuildRegion(currentBounds);
        }

        private void HandleBarricadeSettled(int index, Bounds footprint)
        {
            Bounds previous = barricadeFootprints[index];
            barricadeFootprints[index] = footprint;
            RefreshMovedObstacle(previous, footprint);
        }

        [ContextMenu("Build Preview (Edit Mode)")]
        private void BuildPreview()
        {
            Build();
        }

        [ContextMenu("Clear Preview")]
        private void ClearPreview()
        {
            geometryBlocked = null;
            manualBlocks = null;
            regionIds = null;
            regionsDirty = true;
            IsBuilt = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(boundsCenter, boundsSize);

            if (!drawGizmos || !IsBuilt || geometryBlocked == null)
                return;

            Vector3 blockedSize = Vector3.one * (cellSize * 0.4f);
            Vector3 freeSize = Vector3.one * (cellSize * 0.2f);
            var freeColour = new Color(0f, 1f, 0f, 0.08f);
            var manualColour = new Color(1f, 0.5f, 0f);

            for (int i = 0; i < geometryBlocked.Length; i++)
            {
                Vector3 center = CellToWorld(IndexToCell(i));
                if (manualBlocks[i] > 0)
                {
                    Gizmos.color = manualColour;
                    Gizmos.DrawWireCube(center, blockedSize);
                }
                else if (geometryBlocked[i])
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireCube(center, blockedSize);
                }
                else if (!gizmoOnlyBlocked)
                {
                    Gizmos.color = freeColour;
                    Gizmos.DrawWireCube(center, freeSize);
                }
            }
        }
    }
}
