using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RunwayPanic.AI;
using RunwayPanic.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RunwayPanic.AITools
{
    /// <summary>
    /// Tools > IS > Pathfinding Self-Test: checks the open scene's <see cref="FlightGrid"/> without entering Play mode.
    /// Flies fixed paths across the hangar, the tower and the whole map, plus random pairs; checks that every leg
    /// is clear of level geometry; times the searches; and moves a temporary barricade to check that its cells follow.
    /// Appends the results to <c>Docs/IS/pathfinding_report.md</c> and draws the paths in the Scene view for a minute
    /// (green = passed, red = failed, grey = blocked straight line). Changes no asset; the temporary barricade is removed.
    /// </summary>
    public static class PathfindingSelfTest
    {
        private const string ReportPath = "Docs/IS/pathfinding_report.md";
        private const string BarricadePrefabPath = "Assets/_Project/Prefabs/Interactables/P_Barricade_Movable.prefab";
        private const float MaxSearchMilliseconds = 2f;
        private const int TimedRuns = 20;
        private const int RandomPairs = 200;
        private const int RandomSeed = 12345;
        private const float ValidationRadius = 0.05f;
        private const float DrawSeconds = 60f;

        private sealed class CaseResult
        {
            public string Name;
            public bool Found;
            public bool StraightBlocked;
            public bool LegsClear;
            public bool EndsAtTarget;
            public int Waypoints;
            public int Expanded;
            public float StraightLength;
            public float PathLength;
            public double AverageMs;
            public double WorstMs;
            public bool Passed;
            public string Note = "";
        }

        [MenuItem("Tools/IS/Pathfinding Self-Test")]
        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Pathfinding Self-Test", "Run this in Edit mode (stop Play first).", "OK");
                return;
            }

            FlightGrid grid = UnityEngine.Object.FindAnyObjectByType<FlightGrid>();
            if (grid == null)
            {
                EditorUtility.DisplayDialog("Pathfinding Self-Test", "The open scene has no FlightGrid. Add one first.", "OK");
                return;
            }

            long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
            grid.Build();
            double buildMs = ElapsedMs(buildStart);
            if (!grid.IsBuilt)
                return;

            var provider = new GridPathProvider(grid);
            var path = new List<Vector3>(64);
            var cases = new List<CaseResult>();

            AddObstacleCrossings(grid, provider, path, cases, "Hangar_Greybox", "hangar");
            AddObstacleCrossings(grid, provider, path, cases, "ControlTower_Placeholder", "tower");

            Bounds volume = grid.VolumeBounds;
            Vector3 lowCorner = volume.min + new Vector3(4f, 0f, 4f);
            Vector3 highCorner = volume.max - new Vector3(4f, 0f, 4f);
            float cornerY = Mathf.Max(volume.min.y + 2.5f, 3f);
            cases.Add(RunCase(grid, provider, path, "Corner to corner (SW → NE)",
                new Vector3(lowCorner.x, cornerY, lowCorner.z), new Vector3(highCorner.x, cornerY, highCorner.z), false));
            cases.Add(RunCase(grid, provider, path, "Corner to corner (NW → SE)",
                new Vector3(lowCorner.x, cornerY, highCorner.z), new Vector3(highCorner.x, cornerY, lowCorner.z), false));

            string randomSummary = RunRandomPairs(grid, provider, path, out bool randomPassed);
            string barricadeSummary = RunBarricadeTest(grid, out bool barricadePassed);

            // Leave the grid showing the real level (the barricade test changed some cells).
            grid.Build();
            SceneView.RepaintAll();

            bool allPassed = randomPassed && barricadePassed;
            foreach (CaseResult result in cases)
                allPassed &= result.Passed;

            string report = BuildReport(grid, buildMs, cases, randomSummary, barricadeSummary, allPassed);
            AppendReport(report);

            string verdict = allPassed ? "ALL PASSED" : "FAILURES — see report";
            Debug.Log($"[PathfindingSelfTest] {verdict}. {cases.Count} fixed cases, {RandomPairs} random pairs. Report: {ReportPath}", grid);
        }

        // Two crossings (along X and along Z) through the middle of a named object, at half its height.
        private static void AddObstacleCrossings(FlightGrid grid, GridPathProvider provider, List<Vector3> path,
            List<CaseResult> cases, string objectName, string label)
        {
            GameObject target = GameObject.Find(objectName);
            if (target == null || !TryGetColliderBounds(target, out Bounds bounds))
            {
                cases.Add(new CaseResult { Name = $"Across the {label}", Note = $"'{objectName}' with colliders not found — skipped", Passed = true });
                return;
            }

            float y = Mathf.Max(bounds.center.y, grid.VolumeBounds.min.y + 1.5f);
            Vector3 center = new Vector3(bounds.center.x, y, bounds.center.z);
            float marginX = bounds.extents.x + 6f;
            float marginZ = bounds.extents.z + 6f;

            cases.Add(RunCase(grid, provider, path, $"Across the {label} (X)", InsideGrid(grid, center - Vector3.right * marginX), InsideGrid(grid, center + Vector3.right * marginX), true));
            cases.Add(RunCase(grid, provider, path, $"Across the {label} (Z)", InsideGrid(grid, center - Vector3.forward * marginZ), InsideGrid(grid, center + Vector3.forward * marginZ), true));
        }

        // An obstacle near the edge of the map (the control tower is 2 m from the north fence) would put the far
        // end outside the grid; keep both ends 1 m inside it.
        private static Vector3 InsideGrid(FlightGrid grid, Vector3 point)
        {
            Bounds volume = grid.VolumeBounds;
            Vector3 margin = Vector3.one;
            return Vector3.Max(volume.min + margin, Vector3.Min(volume.max - margin, point));
        }

        private static CaseResult RunCase(FlightGrid grid, GridPathProvider provider, List<Vector3> path,
            string name, Vector3 from, Vector3 to, bool mustBeBlocked)
        {
            var result = new CaseResult
            {
                Name = name,
                StraightLength = Vector3.Distance(from, to),
                StraightBlocked = !PathSmoother.IsClear(from, to, ValidationRadius, grid.ObstacleMask)
            };

            if (!grid.IsFlyable(from) || !grid.IsFlyable(to))
                result.Note = "start or end point is inside geometry; ";

            // Warm-up run (JIT), then timed runs.
            result.Found = provider.TryFindPath(from, to, null, path);
            double total = 0;
            for (int i = 0; i < TimedRuns; i++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                provider.TryFindPath(from, to, null, path);
                double ms = ElapsedMs(start);
                total += ms;
                result.WorstMs = Math.Max(result.WorstMs, ms);
            }

            result.AverageMs = total / TimedRuns;
            result.Expanded = provider.LastExpandedNodes;
            result.Waypoints = path.Count;

            if (result.Found)
            {
                result.LegsClear = CheckLegs(grid, from, path, out result.PathLength);
                result.EndsAtTarget = path.Count > 0 && Vector3.Distance(path[path.Count - 1], to) < 0.01f;
            }

            result.Passed = result.Found && result.LegsClear && result.WorstMs < MaxSearchMilliseconds && (!mustBeBlocked || result.StraightBlocked);
            if (mustBeBlocked && !result.StraightBlocked)
                result.Note += "straight line was already clear, so this case proves nothing; ";

            Draw(from, to, path, result.Found, result.Passed, result.StraightBlocked);
            return result;
        }

        private static string RunRandomPairs(FlightGrid grid, GridPathProvider provider, List<Vector3> path, out bool passed)
        {
            var random = new System.Random(RandomSeed);
            Bounds volume = grid.VolumeBounds;
            int found = 0, unreachable = 0, sealedOff = 0, badLegs = 0, notAtTarget = 0, slow = 0, maxExpanded = 0;
            double total = 0, worst = 0;

            for (int i = 0; i < RandomPairs; i++)
            {
                Vector3 from = RandomFlyablePoint(grid, volume, random);
                Vector3 to = RandomFlyablePoint(grid, volume, random);

                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                bool ok = provider.TryFindPath(from, to, null, path);
                double ms = ElapsedMs(start);
                total += ms;
                worst = Math.Max(worst, ms);
                if (ms >= MaxSearchMilliseconds) slow++;
                maxExpanded = Math.Max(maxExpanded, provider.LastExpandedNodes);

                if (!ok)
                {
                    unreachable++;
                    if (provider.LastWasUnreachable)
                        sealedOff++;
                    Debug.DrawLine(from, to, Color.magenta, DrawSeconds);
                    continue;
                }

                found++;
                if (!CheckLegs(grid, from, path, out _))
                {
                    badLegs++;
                    Draw(from, to, path, true, false, false);
                }

                if (Vector3.Distance(path[path.Count - 1], to) >= 0.01f)
                    notAtTarget++;
            }

            // "No path" alone is not a failure: a random point can lie in a closed room (e.g. a shelter with its door shut).
            passed = badLegs == 0 && slow == 0;
            return string.Format(CultureInfo.InvariantCulture,
                "| {0} | {1} | {2} ({3} sealed off) | {4} | {5} | {6:F3} | {7:F3} | {8} | {9} |",
                RandomPairs, found, unreachable, sealedOff, badLegs, notAtTarget, total / RandomPairs, worst, slow, maxExpanded);
        }

        private static string RunBarricadeTest(FlightGrid grid, out bool passed)
        {
            passed = true;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarricadePrefabPath);
            if (prefab == null)
                return $"Skipped: `{BarricadePrefabPath}` not found.";

            // Two open spots on the runway, 6 m apart (not overlapping).
            var spotA = new Vector3(0f, 0f, -10f);
            var spotB = new Vector3(0f, 0f, -4f);
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, spotA, Quaternion.identity);
                instance.hideFlags = HideFlags.DontSave;
                instance.name = "PathfindingSelfTest_TempBarricade";
                var barricade = instance.GetComponent<Barricade>();

                // Baseline without the barricade (disabled colliders are ignored by physics queries).
                Bounds probeA = BoundsAt(barricade, spotA, instance);
                Bounds probeB = BoundsAt(barricade, spotB, instance);
                grid.Build();
                bool[] baseline = Snapshot(grid);

                // Placed at A.
                instance.transform.position = spotA;
                instance.SetActive(true);
                Physics.SyncTransforms();
                grid.RefreshMovedObstacle(probeA, probeA);
                List<int> blockedAtA = NewlyBlocked(grid, baseline);

                // Moved to B, exactly as the Settled handler does in Play mode.
                instance.transform.position = spotB;
                Physics.SyncTransforms();
                grid.RefreshMovedObstacle(probeA, barricade.Footprint);

                int blockedNearB = 0;
                foreach (int index in NewlyBlocked(grid, baseline))
                    if (ContainsCell(grid, probeB, index))
                        blockedNearB++;

                // Cells the barricade blocked at A that are still blocked now, and not because they are near B.
                int stillBlockedFromA = 0;
                foreach (int index in blockedAtA)
                    if (grid.IsBlockedIndex(index) && !ContainsCell(grid, probeB, index))
                        stillBlockedFromA++;

                if (blockedAtA.Count == 0)
                {
                    passed = false;
                    float top = probeA.max.y;
                    return $"FAILED: the barricade (top at {top:F2} m) blocked **0** cells. Its height lies inside a cell layer " +
                           $"that the ground already blocks — raise the grid floor (Bounds Center Y) so the lowest layer clears the ground.";
                }

                passed = blockedNearB > 0 && stillBlockedFromA == 0;
                return $"{(passed ? "PASSED" : "FAILED")}: at A it blocked **{blockedAtA.Count}** cells; after moving 6 m to B " +
                       $"it blocks **{blockedNearB}** cells there and **{stillBlockedFromA}** cells stay wrongly blocked at A.";
            }
            finally
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                Physics.SyncTransforms();
            }
        }

        // Footprint the barricade would have at 'position' (briefly enabled to read collider bounds).
        private static Bounds BoundsAt(Barricade barricade, Vector3 position, GameObject instance)
        {
            instance.transform.position = position;
            instance.SetActive(true);
            Physics.SyncTransforms();
            Bounds bounds = barricade.Footprint;
            instance.SetActive(false);
            return bounds;
        }

        private static bool[] Snapshot(FlightGrid grid)
        {
            var blocked = new bool[grid.CellCount];
            for (int i = 0; i < blocked.Length; i++)
                blocked[i] = grid.IsBlockedIndex(i);
            return blocked;
        }

        private static List<int> NewlyBlocked(FlightGrid grid, bool[] baseline)
        {
            var result = new List<int>();
            for (int i = 0; i < baseline.Length; i++)
                if (!baseline[i] && grid.IsBlockedIndex(i))
                    result.Add(i);
            return result;
        }

        private static bool ContainsCell(FlightGrid grid, Bounds bounds, int index)
        {
            bounds.Expand(grid.CellSize * 2f);
            return bounds.Contains(grid.CellToWorld(grid.IndexToCell(index)));
        }

        // Every leg (from → p0 → p1 …) must be clear of level geometry.
        private static bool CheckLegs(FlightGrid grid, Vector3 from, List<Vector3> path, out float length)
        {
            length = 0f;
            bool clear = true;
            Vector3 previous = from;
            foreach (Vector3 point in path)
            {
                length += Vector3.Distance(previous, point);
                if (!PathSmoother.IsClear(previous, point, ValidationRadius, grid.ObstacleMask))
                    clear = false;
                previous = point;
            }

            return clear;
        }

        private static Vector3 RandomFlyablePoint(FlightGrid grid, Bounds volume, System.Random random)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var point = new Vector3(
                    Mathf.Lerp(volume.min.x + 1f, volume.max.x - 1f, (float)random.NextDouble()),
                    Mathf.Lerp(volume.min.y + 1f, volume.max.y - 1f, (float)random.NextDouble()),
                    Mathf.Lerp(volume.min.z + 1f, volume.max.z - 1f, (float)random.NextDouble()));
                if (grid.IsFlyable(point))
                    return point;
            }

            return volume.center;
        }

        private static bool TryGetColliderBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            Collider[] colliders = target.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
                return false;

            bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
                bounds.Encapsulate(colliders[i].bounds);
            return true;
        }

        private static void Draw(Vector3 from, Vector3 to, List<Vector3> path, bool found, bool passed, bool straightBlocked)
        {
            if (straightBlocked)
                Debug.DrawLine(from, to, new Color(0.6f, 0.6f, 0.6f), DrawSeconds);
            if (!found)
                return;

            Color colour = passed ? Color.green : Color.red;
            Vector3 previous = from;
            foreach (Vector3 point in path)
            {
                Debug.DrawLine(previous, point, colour, DrawSeconds);
                previous = point;
            }
        }

        private static string BuildReport(FlightGrid grid, double buildMs, List<CaseResult> cases,
            string randomRow, string barricadeSummary, bool allPassed)
        {
            var inv = CultureInfo.InvariantCulture;
            Bounds volume = grid.VolumeBounds;
            var sb = new StringBuilder();
            sb.AppendLine($"## Run {DateTime.Now.ToString("yyyy-MM-dd HH:mm", inv)} — {(allPassed ? "ALL PASSED" : "FAILURES")}");
            sb.AppendLine();
            sb.AppendLine($"- Scene: `{EditorSceneManager.GetActiveScene().path}` (Edit mode, Unity {Application.unityVersion})");
            sb.AppendLine(string.Format(inv, "- Grid: {0} × {1} × {2} = {3:N0} cells of {4} m; volume y {5:F1} → {6:F1} m; clearance {7} m; {8:N0} blocked; {9} free-air region(s); built in {10:F1} ms",
                grid.Size.x, grid.Size.y, grid.Size.z, grid.CellCount, grid.CellSize, volume.min.y, volume.max.y, grid.Clearance, grid.BlockedCellCount, grid.RegionCount, buildMs));
            sb.AppendLine($"- A*: 26 neighbours, no corner cutting, octile heuristic × {AStarPathfinder.TieBreak} tie-break, limit {grid.MaxExpandedNodes:N0} cells, sealed-off goals rejected by region check");
            sb.AppendLine($"- Pass rules: path found; every leg clear of geometry (sphere {ValidationRadius} m); worst of {TimedRuns} timed searches < {MaxSearchMilliseconds} ms; obstacle crossings must have a blocked straight line");
            sb.AppendLine();
            sb.AppendLine("| Case | Result | Straight line blocked | Legs clear | Ends at target | Waypoints | Cells expanded | Straight (m) | Path (m) | Avg ms | Worst ms | Note |");
            sb.AppendLine("|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (CaseResult r in cases)
            {
                sb.AppendLine(string.Format(inv, "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7:F1} | {8:F1} | {9:F3} | {10:F3} | {11} |",
                    r.Name, r.Passed ? "PASS" : "**FAIL**", YesNo(r.StraightBlocked), YesNo(r.LegsClear), YesNo(r.EndsAtTarget),
                    r.Waypoints, r.Expanded, r.StraightLength, r.PathLength, r.AverageMs, r.WorstMs, r.Note.TrimEnd(' ', ';')));
            }

            sb.AppendLine();
            sb.AppendLine($"Random pairs (seed {RandomSeed}, flyable points anywhere in the volume; fails only on legs through geometry or searches ≥ {MaxSearchMilliseconds} ms; " +
                          $"\"no path\" can be a closed room or the {grid.MaxExpandedNodes:N0}-cell search limit, drawn in magenta):");
            sb.AppendLine();
            sb.AppendLine("| Pairs | Found | No path | Legs through geometry | Not ending at target | Avg ms | Worst ms | ≥ 2 ms | Max cells expanded |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            sb.AppendLine(randomRow);
            sb.AppendLine();
            sb.AppendLine($"Barricade (temporary `P_Barricade_Movable` on the runway, moved 6 m): {barricadeSummary}");
            sb.AppendLine();
            return sb.ToString();
        }

        private static void AppendReport(string section)
        {
            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            if (!File.Exists(fullPath))
            {
                File.WriteAllText(fullPath,
                    "# Pathfinding self-test report\n\n" +
                    "Generated by `Tools > IS > Pathfinding Self-Test` (`Assets/_Project/Editor/AI/PathfindingSelfTest.cs`). " +
                    "Each run is appended below. Times are Unity Editor wall-clock (Stopwatch), single searches, no cost modifiers.\n\n");
            }

            File.AppendAllText(fullPath, section);
        }

        private static string YesNo(bool value) => value ? "yes" : "no";

        private static double ElapsedMs(long startTicks)
        {
            return (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }
    }
}
