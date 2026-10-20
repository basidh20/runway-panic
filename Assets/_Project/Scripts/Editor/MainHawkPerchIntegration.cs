using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Agents;
using RunwayPanic.Agents.Hawk;
using RunwayPanic.AI;
using RunwayPanic.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Main-scene counterparts of the Hawk owner's sandbox markers; no brain or shared-tool changes.</summary>
    public static class MainHawkPerchIntegration
    {
        private const string RootName = "HawkPerches";
        private static readonly (string name, Vector3 position, string support)[] Perches =
        {
            ("Perch_ControlTower", new Vector3(-30.9f, 15.09f, 25.9f), "ControlTower_Placeholder"), // new tower roof (20 Oct, ArtControlTowerBuilder)
            ("Perch_TerminalRoof", new Vector3(20, 6.4f, 25.9f), "TerminalRoof"),
            ("Perch_Hangar_NE", new Vector3(-31.9f, 6, -.6f), "Hangar_Greybox"),
            ("Perch_Hangar_SE", new Vector3(-31.9f, 6, -24.8f), "Hangar_Greybox"),
            // Aircraft models (20 Oct): tailplane collider top + 0.02 m, local (0, 3.05, -3.89). Plane A is the point
            // Enlarge Plane A already aligned on its 1.35x model; B/D are 90 deg-turned 1x models (root + 3.89 m in X).
            ("Perch_PlaneA_Tail", new Vector3(-16.987864f, 4.1375003f, -29.301777f), "Plane_A_Greybox"),
            ("Perch_PlaneB_Tail", new Vector3(37.19f, 3.07f, -1), "Plane_B_Greybox"),
            ("Perch_PlaneD_Tail", new Vector3(41.89f, 3.07f, 23), "Plane_D_Greybox"),
            ("Perch_ShelterB_Roof", new Vector3(21, 4.3f, -12), "Shelter_B")
        };

        [MenuItem("Tools/Runway Panic/Integrate Main Hawk Perches")]
        public static void Integrate()
        {
            RequireMain();
            var roots = SceneManager.GetActiveScene().GetRootGameObjects().Where(root => root.name == RootName).ToArray();
            if (roots.Length > 1 || MainPlayerIntegration.InScene<FlightGrid>(true).Length != 1)
                throw new InvalidOperationException("Expected one FlightGrid and at most one HawkPerches root.");
            var root = roots.SingleOrDefault();
            if (root != null && (root.transform.Cast<Transform>().Any(child => !Perches.Any(spec => spec.name == child.name)) ||
                Perches.Any(spec => root.transform.Cast<Transform>().Count(child => child.name == spec.name) > 1)))
                throw new InvalidOperationException("Unexpected/duplicate perch children; review before integration.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place Hawk vantage markers in Main");
            try
            {
                if (root == null)
                {
                    root = new GameObject(RootName);
                    Undo.RegisterCreatedObjectUndo(root, "Add Hawk marker group");
                }
                foreach (var spec in Perches)
                {
                    var point = root.transform.Find(spec.name);
                    if (point == null)
                    {
                        var item = new GameObject(spec.name);
                        Undo.RegisterCreatedObjectUndo(item, "Add Hawk vantage marker");
                        item.transform.SetParent(root.transform, false);
                        point = item.transform;
                    }
                    Undo.RecordObject(point, "Apply handoff position");
                    point.position = spec.position;
                    var marker = point.GetComponent<BirdPoint>();
                    if (marker == null) marker = Undo.AddComponent<BirdPoint>(point.gameObject);
                    var data = new SerializedObject(marker);
                    data.FindProperty("type").intValue = (int)BirdPointType.Perch;
                    data.FindProperty("radius").floatValue = 1.5f;
                    data.FindProperty("group").stringValue = "HawkVantage";
                    data.ApplyModifiedProperties();
                }
                Undo.FlushUndoRecordObjects();
                Validate();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save Main.");
                Debug.Log("[MainHawkPerches] Saved eight shared Perch markers at the Hawk owner's handoff locations.");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        [MenuItem("Tools/Runway Panic/Validate Main Hawk Perches")]
        public static void Validate()
        {
            RequireMain();
            var lines = new List<string> { "Main Hawk perches — focused Edit-mode marker/surface/soaring-clearance check", "Unity: " + Application.unityVersion };
            int failures = 0, visible = 0;
            Action<bool, string> check = (ok, message) => { lines.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; };
            var root = GameObject.Find(RootName);
            var config = AssetDatabase.LoadAssetAtPath<HawkStrategyConfig>("Assets/_Project/ScriptableObjects/Birds/Strategy/HawkStrategyConfig.asset");
            var grid = MainPlayerIntegration.InScene<FlightGrid>().Single();
            var player = MainPlayerIntegration.InScene<PlayerController>().Single();
            var sun = MainPlayerIntegration.InScene<Light>().First(light => light.type == LightType.Directional);
            if (root == null || config == null) throw new InvalidOperationException("Missing HawkPerches or shared Hawk config.");
            Physics.SyncTransforms(); grid.Build(); // Preview arrays are not serialized and do not alter the grid's settings.
            check(root.transform.childCount == 8 && root.GetComponentsInChildren<BirdPoint>(true).Length == 8,
                "Eight named markers under one HawkPerches root");
            foreach (var spec in Perches)
            {
                var point = root.transform.Find(spec.name);
                var marker = point != null ? point.GetComponent<BirdPoint>() : null;
                check(marker != null && marker.isActiveAndEnabled && marker.Type == BirdPointType.Perch && marker.Radius == 1.5f &&
                    marker.Group == "HawkVantage" && Vector3.Distance(marker.Position, spec.position) < .001f &&
                    point.GetComponentsInChildren<Collider>(true).Length == 0 && point.GetComponents<BirdPoint>().Length == 1,
                    spec.name + " is a non-colliding Perch at " + spec.position.ToString("F2"));
                if (marker == null) continue;
                var support = GameObject.Find(spec.support);
                bool hitSurface = Physics.Raycast(marker.Position + Vector3.up * .15f, Vector3.down, out var hit, .3f, ~0, QueryTriggerInteraction.Ignore);
                check(hitSurface && support != null && hit.collider.transform.IsChildOf(support.transform) &&
                    Mathf.Abs(hit.point.y - marker.Position.y) < .1f && hit.normal.y > .9f, spec.name + " matches the intended roof/tail surface");
                Vector3 centre = StoopPlanner.ClampCircleToVolume(marker.Position + Vector3.up * config.SoarHeight,
                    config.SoarRadius, grid.VolumeBounds, grid.CellSize);
                bool clear = grid.IsFlyable(centre);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4;
                    clear &= grid.IsFlyable(centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * config.SoarRadius);
                }
                check(clear, spec.name + " soar centre and eight circle samples have clear flight space using shared config");
                var score = PerchSelector.Score(marker, player.transform.position, player.transform.position + Vector3.up * 1.5f,
                    sun.transform.forward, null, config, false, config.SoarHeight, grid.ObstacleMask);
                check(!float.IsNaN(score.Total) && !float.IsInfinity(score.Total), spec.name + " scores through the shared Hawk utility selector");
                if (score.LineOfSight > .5f) visible++;
            }
            check(visible > 0, "At least one vantage sees the original player start; others may offer cover");
            var preview = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(item => item.name == "GreyboxPreview_TEMPORARY");
            check(preview != null && !preview.activeSelf, "Old walkthrough rig stays disabled");
            lines.Add("Edit-mode geometry/selector check only. No Hawk spawned, full flight simulation or AI/stress test. Eight circle samples do not certify every point of an orbit.");
            lines.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/WorldBuilder");
            File.WriteAllLines("Docs/WorldBuilder/HawkPerchValidation.txt", lines);
            if (failures != 0) throw new InvalidOperationException("Hawk perch check failed; see HawkPerchValidation.txt.");
        }

        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
        }
    }
}
