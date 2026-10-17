using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// Single entry point for bird pathfinding. <see cref="Current"/> is A* on the scene's <see cref="FlightGrid"/>,
    /// or a straight-line fallback when there is none.
    /// <see cref="TryConsumeBudget"/> time-slices the searches: only a few run per frame, the rest wait a frame,
    /// so a whole wave asking for paths at once can't cause a frame spike.
    /// </summary>
    public static class PathService
    {
        /// <summary>Searches per frame when there is no <see cref="FlightGrid"/> to read the setting from.</summary>
        public const int DefaultMaxSearchesPerFrame = 6;

        private static GridPathProvider gridProvider;
        private static StraightLinePathProvider straightLineProvider;
        private static int budgetFrame = -1;
        private static int searchesThisFrame;

        /// <summary>The provider brains should use right now.</summary>
        public static IPathProvider Current
        {
            get
            {
                FlightGrid grid = FlightGrid.Instance;
                if (grid != null)
                {
                    if (gridProvider == null || gridProvider.Grid != grid)
                        gridProvider = new GridPathProvider(grid);
                    return gridProvider;
                }

                return straightLineProvider ??= new StraightLinePathProvider();
            }
        }

        /// <summary>A* searches allowed per frame (set on the <see cref="FlightGrid"/>).</summary>
        public static int MaxSearchesPerFrame =>
            FlightGrid.Instance != null ? FlightGrid.Instance.MaxSearchesPerFrame : DefaultMaxSearchesPerFrame;

        /// <summary>Searches already granted this frame.</summary>
        public static int SearchesThisFrame
        {
            get
            {
                ResetIfNewFrame();
                return searchesThisFrame;
            }
        }

        /// <summary>
        /// Call before every search. Returns false when this frame's budget is used up; the caller should keep
        /// its current path and try again next frame.
        /// </summary>
        public static bool TryConsumeBudget()
        {
            ResetIfNewFrame();
            if (searchesThisFrame >= MaxSearchesPerFrame)
                return false;

            searchesThisFrame++;
            return true;
        }

        private static void ResetIfNewFrame()
        {
            if (budgetFrame == Time.frameCount)
                return;

            budgetFrame = Time.frameCount;
            searchesThisFrame = 0;
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            gridProvider = null;
            straightLineProvider = null;
            budgetFrame = -1;
            searchesThisFrame = 0;
        }
    }
}
