using RunwayPanic.AI;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Influence map of remembered danger for crows: a 2D grid (XZ) of floats over the airport.
    /// Danger is "stamped" where crows were shot from; crows pass the map to A* as an <see cref="ICostModifier"/>,
    /// so their paths bend around the player's firing positions. One per scene, created by the first crow.
    /// <para>
    /// Split into partial files: this core (grid, stamping, sampling), Grudge (marks from crow deaths),
    /// Decay (forgetting over time and between waves) and Debug (heatmap gizmo).
    /// </para>
    /// <para>Why it keeps A* optimal: the extra cost is always ≥ 0 and the heuristic is unchanged, so the
    /// heuristic still never overestimates the (now larger) true cost — A* stays admissible.</para>
    /// </summary>
    public sealed partial class CrowDangerMap : MonoBehaviour, ICostModifier
    {
        private const float FallbackSize = 200f;
        private const float NegligibleDanger = 0.01f;

        private CrowStrategyConfig config;
        private float[] cells;
        private int sizeX;
        private int sizeZ;
        private float cellSize;
        private Vector2 origin; // world XZ of the grid's minimum corner

        /// <summary>The scene's danger map, or null before any crow created it.</summary>
        public static CrowDangerMap Instance { get; private set; }

        /// <summary>Tuning in use.</summary>
        public CrowStrategyConfig Config => config;

        /// <summary>Number of cells along X and Z.</summary>
        public Vector2Int Size => new Vector2Int(sizeX, sizeZ);

        /// <summary>Size of one cell in metres.</summary>
        public float CellSize => cellSize;

        /// <summary>
        /// Returns the scene's map, creating it (on its own GameObject) if needed. The first caller's config is used.
        /// </summary>
        public static CrowDangerMap GetOrCreate(CrowStrategyConfig strategyConfig)
        {
            if (Instance != null)
                return Instance;

            var map = new GameObject("[CrowDangerMap]").AddComponent<CrowDangerMap>();
            map.Initialise(strategyConfig);
            return map;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            SubscribeGrudge();
            SubscribeDecay();
        }

        private void OnDisable()
        {
            UnsubscribeGrudge();
            UnsubscribeDecay();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            TickDecay();
        }

        /// <summary>
        /// Sizes the grid to the flight grid's footprint (or 200 × 200 m around the origin) and clears it.
        /// </summary>
        public void Initialise(CrowStrategyConfig strategyConfig)
        {
            config = strategyConfig;
            cellSize = config != null ? config.dangerCellSize : 2f;

            Bounds area = FlightGrid.Instance != null
                ? FlightGrid.Instance.VolumeBounds
                : new Bounds(Vector3.zero, new Vector3(FallbackSize, 0f, FallbackSize));

            origin = new Vector2(area.min.x, area.min.z);
            sizeX = Mathf.Max(1, Mathf.CeilToInt(area.size.x / cellSize));
            sizeZ = Mathf.Max(1, Mathf.CeilToInt(area.size.z / cellSize));
            cells = new float[sizeX * sizeZ];
        }

        /// <summary>
        /// Adds danger in a disc: <paramref name="strength"/> at the centre falling linearly to 0 at
        /// <paramref name="radius"/>. Each cell is clamped to the config's max danger.
        /// </summary>
        public void Stamp(Vector3 centre, float radius, float strength)
        {
            if (cells == null || radius <= 0f || strength <= 0f)
                return;

            ForCellsInBox(centre, centre, radius, (index, cellCentre) =>
            {
                float d = Vector2.Distance(cellCentre, new Vector2(centre.x, centre.z));
                return d < radius ? strength * (1f - d / radius) : 0f;
            });
        }

        /// <summary>
        /// Adds danger along the segment <paramref name="a"/> → <paramref name="b"/> (the line of fire),
        /// falling linearly with distance from the segment to 0 at <paramref name="radius"/>.
        /// </summary>
        public void StampLine(Vector3 a, Vector3 b, float radius, float strength)
        {
            if (cells == null || radius <= 0f || strength <= 0f)
                return;

            Vector2 a2 = new Vector2(a.x, a.z);
            Vector2 b2 = new Vector2(b.x, b.z);
            ForCellsInBox(a, b, radius, (index, cellCentre) =>
            {
                float d = DistanceToSegment(cellCentre, a2, b2);
                return d < radius ? strength * (1f - d / radius) : 0f;
            });
        }

        /// <summary>Danger at a world position, bilinearly interpolated between the four nearest cell centres.</summary>
        public float GetDanger(Vector3 worldPosition)
        {
            if (cells == null)
                return 0f;

            // Continuous cell coordinates measured from cell centres.
            float gx = (worldPosition.x - origin.x) / cellSize - 0.5f;
            float gz = (worldPosition.z - origin.y) / cellSize - 0.5f;
            int x0 = Mathf.FloorToInt(gx);
            int z0 = Mathf.FloorToInt(gz);
            float tx = gx - x0;
            float tz = gz - z0;

            float d00 = CellValue(x0, z0);
            float d10 = CellValue(x0 + 1, z0);
            float d01 = CellValue(x0, z0 + 1);
            float d11 = CellValue(x0 + 1, z0 + 1);
            return Mathf.Lerp(Mathf.Lerp(d00, d10, tx), Mathf.Lerp(d01, d11, tx), tz);
        }

        /// <summary>A* extra cost for entering a cell here: danger × <c>dangerCostScale</c>. Always ≥ 0.</summary>
        public float GetExtraCost(Vector3 worldPosition)
        {
            float danger = GetDanger(worldPosition);
            return danger > 0f ? danger * (config != null ? config.dangerCostScale : 20f) : 0f;
        }

        /// <summary>Multiplies every cell by <paramref name="factor"/>; values that become negligible are zeroed.</summary>
        public void Scale(float factor)
        {
            if (cells == null)
                return;

            for (int i = 0; i < cells.Length; i++)
            {
                float v = cells[i] * factor;
                cells[i] = v < NegligibleDanger ? 0f : v;
            }
        }

        /// <summary>Removes all danger.</summary>
        public void Clear()
        {
            if (cells != null)
                System.Array.Clear(cells, 0, cells.Length);
        }

        /// <summary>Sum of all cells (for stats).</summary>
        public float TotalDanger
        {
            get
            {
                float sum = 0f;
                if (cells != null)
                    for (int i = 0; i < cells.Length; i++)
                        sum += cells[i];
                return sum;
            }
        }

        /// <summary>Highest single cell (for stats).</summary>
        public float HottestCell
        {
            get
            {
                float max = 0f;
                if (cells != null)
                    for (int i = 0; i < cells.Length; i++)
                        if (cells[i] > max)
                            max = cells[i];
                return max;
            }
        }

        /// <summary>World centre (at y = 0) of cell (x, z).</summary>
        public Vector3 CellCentre(int x, int z) =>
            new Vector3(origin.x + (x + 0.5f) * cellSize, 0f, origin.y + (z + 0.5f) * cellSize);

        /// <summary>Raw value of cell (x, z); 0 outside the grid.</summary>
        public float CellValue(int x, int z) =>
            x < 0 || z < 0 || x >= sizeX || z >= sizeZ ? 0f : cells[x + z * sizeX];

        // Applies a falloff function to every cell in the box around a..b grown by radius, adding and clamping.
        private void ForCellsInBox(Vector3 a, Vector3 b, float radius, System.Func<int, Vector2, float> amountAt)
        {
            float max = config != null ? config.maxDanger : 5f;
            int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius - origin.x) / cellSize));
            int maxX = Mathf.Min(sizeX - 1, Mathf.FloorToInt((Mathf.Max(a.x, b.x) + radius - origin.x) / cellSize));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - radius - origin.y) / cellSize));
            int maxZ = Mathf.Min(sizeZ - 1, Mathf.FloorToInt((Mathf.Max(a.z, b.z) + radius - origin.y) / cellSize));

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = x + z * sizeX;
                    Vector3 c = CellCentre(x, z);
                    float add = amountAt(index, new Vector2(c.x, c.z));
                    if (add > 0f)
                        cells[index] = Mathf.Min(max, cells[index] + add);
                }
            }
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f)
                return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return Vector2.Distance(p, a + ab * t);
        }

        // ---- Hooks implemented by the other partial files ----
        partial void SubscribeGrudge();
        partial void UnsubscribeGrudge();
        partial void SubscribeDecay();
        partial void UnsubscribeDecay();
        partial void TickDecay();
    }
}
