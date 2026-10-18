using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Agents;
using RunwayPanic.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Main-only placement of the shared flight grid and existing spawn markers. No new navigation logic.</summary>
    [InitializeOnLoad]
    public static class MainFlightIntegration
    {
        private const string SessionKey = "RunwayPanic.MainFlightSmoke";
        private const string ReportPath = "Docs/WorldBuilder/FlightValidation.txt";
        private static readonly List<string> Report = new List<string>();
        private static readonly List<Vector3> Waypoints = new List<Vector3>();
        private static readonly Vector3 RunwayGoal = new Vector3(0, 6, 0);
        private static FlightGrid grid;
        private static Transform[] markers;
        private static bool running, ready;
        private static int query, failures;
        private static double began;

        static MainFlightIntegration() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Tools/Runway Panic/Integrate Main Flight Grid and Spawns")]
        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
            var points = FindMarkers();
            var grids = MainPlayerIntegration.InScene<FlightGrid>(true);
            if (points.Length != 8 || grids.Length > 1 || points.Any(point => point.GetComponents<BirdPoint>().Length > 1))
                throw new InvalidOperationException("Expected eight original markers and at most one grid/component per marker. No changes made.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Connect airport flight grid and spawn markers");
            try
            {
                FlightGrid instance;
                if (grids.Length == 0)
                {
                    var root = new GameObject("FlightGrid");
                    Undo.RegisterCreatedObjectUndo(root, "Add shared flight grid");
                    instance = Undo.AddComponent<FlightGrid>(root);
                }
                else instance = grids[0];
                var settings = new SerializedObject(instance);
                settings.FindProperty("boundsCenter").vector3Value = new Vector3(0, 12.5f, 0);
                settings.FindProperty("boundsSize").vector3Value = new Vector3(100, 24, 80);
                settings.FindProperty("cellSize").floatValue = 2;
                settings.FindProperty("clearance").floatValue = .4f;
                settings.FindProperty("obstacleMask").intValue = -1;
                settings.FindProperty("flyableRadius").floatValue = .3f;
                settings.FindProperty("maxExpandedNodes").intValue = 8000;
                settings.FindProperty("maxSearchesPerFrame").intValue = 6;
                settings.ApplyModifiedProperties();
                foreach (var point in points)
                {
                    var marker = point.GetComponent<BirdPoint>();
                    if (marker == null) marker = Undo.AddComponent<BirdPoint>(point.gameObject);
                    var data = new SerializedObject(marker);
                    data.FindProperty("type").intValue = (int)BirdPointType.Spawn;
                    data.FindProperty("radius").floatValue = 1.5f;
                    data.FindProperty("group").stringValue = "AirportSpawn";
                    data.ApplyModifiedProperties();
                }
                Undo.FlushUndoRecordObjects();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save Main.");
                Debug.Log("[MainFlight] Saved one shared FlightGrid and eight typed spawn markers. No birds or wave manager added.");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        [MenuItem("Tools/Runway Panic/Run Main Flight Smoke Check")]
        public static void RunSmokeCheck()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath ||
                SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Open and save Main outside Play mode first.");
            SessionState.SetBool(SessionKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                query = failures = 0; ready = false; running = true;
                began = EditorApplication.timeSinceStartup;
                Report.Clear();
                Report.Add("Main flight integration — one focused runtime grid/marker/path check");
                Report.Add("Unity: " + Application.unityVersion);
                Report.Add("Uses the real PathService and shared A*. No enemies spawned, no full AI/stress test.");
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && running)
            { Check(false, "Check interrupted"); Finish(false); }
            else if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(SessionKey, false);
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 20) throw new TimeoutException("Flight check exceeded 20 seconds.");
                if (EditorApplication.timeSinceStartup - began < 2) return;
                if (!ready)
                {
                    grid = MainPlayerIntegration.InScene<FlightGrid>(true).Single();
                    markers = FindMarkers();
                    Check(grid.enabled && grid.gameObject.activeInHierarchy && FlightGrid.Instance == grid && grid.IsBuilt,
                        "One active shared grid built automatically on scene start");
                    Check(grid.VolumeBounds.center == new Vector3(0, 12.5f, 0) && grid.VolumeBounds.size == new Vector3(100, 24, 80) &&
                        grid.CellSize == 2 && grid.Clearance == .4f && grid.CellCount == 24000, "Airport volume and 24,000-cell resolution");
                    Check(grid.BlockedCellCount > 0 && grid.BlockedCellCount < grid.CellCount, "Grid recognises both airport obstacles and free air");
                    Check(PathService.Current is GridPathProvider && PathService.MaxSearchesPerFrame == 6, "PathService selects grid A*, not straight-line fallback; six searches/frame");
                    var registered = new List<BirdPoint>(); BirdPoint.GetAll(BirdPointType.Spawn, registered);
                    Check(markers.Length == 8 && registered.Count == 8, "Eight original markers register as spawn points");
                    var preview = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(root => root.name == "GreyboxPreview_TEMPORARY");
                    Check(preview != null && !preview.activeSelf, "Old walkthrough rig remains disabled");
                    Report.Add("INFO: Blocked cells " + grid.BlockedCellCount + "; free-air regions " + grid.RegionCount);
                    ready = true;
                }
                while (query < 10)
                {
                    if (!PathService.TryConsumeBudget()) return; // Respect the production per-frame search budget.
                    if (query < 8)
                    {
                        var marker = markers[query]; var point = marker.GetComponent<BirdPoint>();
                        Check(point != null && point.Type == BirdPointType.Spawn && point.Group == "AirportSpawn" && point.Radius == 1.5f &&
                            grid.VolumeBounds.Contains(marker.position) && grid.IsFlyable(marker.position), marker.name + " typed spawn is in clear airport air at " + marker.position.ToString("F2"));
                        bool found = PathService.Current.TryFindPath(marker.position, RunwayGoal, null, Waypoints);
                        Check(found && Waypoints.Count > 0 && Vector3.Distance(Waypoints[Waypoints.Count - 1], RunwayGoal) < .01f,
                            marker.name + " has an A* route to open runway air");
                    }
                    else
                    {
                        string name = query == 8 ? "Shelter_A" : "Shelter_B";
                        var shelter = GameObject.Find("Environment/Buildings/" + name);
                        if (shelter == null) throw new InvalidOperationException("Missing " + name);
                        bool found = PathService.Current.TryFindPath(RunwayGoal, shelter.transform.TransformPoint(new Vector3(0, 1.5f, 0)), null, Waypoints);
                        Check(!found, name + " interior is not routed through the narrow doorway from outdoor air (not an invulnerability test)");
                    }
                    query++;
                }
                Finish();
            }
            catch (Exception exception) { Check(false, exception.ToString()); Finish(); }
        }

        private static Transform[] FindMarkers() => Enumerable.Range(1, 8)
            .Select(i => GameObject.Find("GameplayMarkers/BirdSpawnPoints/BirdSpawn_" + i.ToString("00"))?.transform)
            .Where(point => point != null).ToArray();
        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(bool exitPlay = true)
        {
            running = false; EditorApplication.update -= Tick;
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllLines(ReportPath, Report);
            Debug.Log("[MainFlight] Smoke check finished. Failures: " + failures + "; " + ReportPath);
            if (exitPlay) EditorApplication.isPlaying = false;
        }
    }
}
