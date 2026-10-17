using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// Test tool: searches a path between two Transforms every few tenths of a second and draws it
    /// (cyan line in the Scene view; enable Gizmos to see it in the Game view). Move either Transform in
    /// Play mode to watch the path update. The Inspector shows the last search's time and expanded cells.
    /// Calls the provider directly, so it ignores the per-frame search budget.
    /// </summary>
    public sealed class PathDebugProbe : MonoBehaviour
    {
        [Tooltip("Where the path starts.")]
        [SerializeField] private Transform start;
        [Tooltip("Where the path should end.")]
        [SerializeField] private Transform end;
        [Tooltip("Seconds between searches.")]
        [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;

        [Header("Last search (read-only)")]
        [Tooltip("Whether a path was found.")]
        [SerializeField] private bool found;
        [Tooltip("Waypoints after smoothing.")]
        [SerializeField] private int waypointCount;
        [Tooltip("Cells A* expanded.")]
        [SerializeField] private int expandedCells;
        [Tooltip("Wall-clock time of the search, in milliseconds (includes smoothing).")]
        [SerializeField] private float searchMilliseconds;
        [Tooltip("Slowest search since Play started, in milliseconds.")]
        [SerializeField] private float worstMilliseconds;

        private readonly List<Vector3> path = new List<Vector3>(64);
        private float nextSearchTime;

        private void Update()
        {
            if (start == null || end == null)
                return;

            if (Time.time >= nextSearchTime)
            {
                nextSearchTime = Time.time + searchInterval;
                Search();
            }

            if (!found)
                return;

            Vector3 previous = start.position;
            for (int i = 0; i < path.Count; i++)
            {
                Debug.DrawLine(previous, path[i], Color.cyan);
                previous = path[i];
            }
        }

        private void Search()
        {
            IPathProvider provider = PathService.Current;

            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            found = provider.TryFindPath(start.position, end.position, null, path);
            long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - startTicks;

            searchMilliseconds = (float)(ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            worstMilliseconds = Mathf.Max(worstMilliseconds, searchMilliseconds);
            waypointCount = path.Count;
            expandedCells = provider is GridPathProvider grid ? grid.LastExpandedNodes : 0;
        }

        private void OnDrawGizmos()
        {
            if (start != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(start.position, 0.4f);
            }

            if (end != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(end.position, 0.4f);
            }

            if (!found)
                return;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < path.Count; i++)
                Gizmos.DrawWireSphere(path[i], 0.25f);
        }
    }
}
