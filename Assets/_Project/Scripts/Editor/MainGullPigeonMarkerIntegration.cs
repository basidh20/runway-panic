using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Agents;
using RunwayPanic.Agents.Gull;
using RunwayPanic.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Level reference points from the Gull/Pigeon plans, not either species' runtime implementation.</summary>
    public static class MainGullPigeonMarkerIntegration
    {
        private const string RootName = "GullPigeonBehaviourPoints";

        private static (string name, BirdPointType type, Vector3 position, float radius, string group, string support)[] Specs()
        {
            var plane = GameObject.Find("Plane_A_Greybox");
            if (plane == null) throw new InvalidOperationException("Expected the existing utility aircraft A.");
            var points = new List<(string, BirdPointType, Vector3, float, string, string)>
            {
                ("KettleCenter_Runway", BirdPointType.KettleCenter, new Vector3(0, 10, 0), 8, "GullKettle", ""),
                ("Roost_HangarRoof", BirdPointType.Roost, new Vector3(-38.46f, 6, -12.7f), 2, "PigeonRoost", "Hangar_Greybox"),
                ("Roost_TerminalRoof", BirdPointType.Roost, new Vector3(20, 6.4f, 30), 2, "PigeonRoost", "TerminalRoof"),
                // Away from the fuselage and engine; a small ring fits the swept wing's actual surface.
                ("Roost_PlaneA_Wing", BirdPointType.Roost, plane.transform.TransformPoint(new Vector3(3.1f, 1.35f, -.15f)), .6f, "PigeonRoost", "Plane_A_Greybox")
            };
            foreach (float x in new[] { -8.5f, 8.5f })
                foreach (float z in new[] { -30f, -10f, 10f, 30f })
                    points.Add(("FodSpawn_" + (x < 0 ? "West_" : "East_") + (z < 0 ? "South" : "North") + Mathf.Abs(z),
                        BirdPointType.FodSpawn, new Vector3(x, 0, z), .5f, "GullFod", "Ground_100x80"));
            return points.ToArray();
        }

        [MenuItem("Tools/Runway Panic/Integrate Main Gull and Pigeon Markers")]
        public static void Integrate()
        {
            RequireMain();
            var specs = Specs();
            var roots = SceneManager.GetActiveScene().GetRootGameObjects().Where(item => item.name == RootName).ToArray();
            if (roots.Length > 1 || MainPlayerIntegration.InScene<FlightGrid>(true).Length != 1)
                throw new InvalidOperationException("Expected one FlightGrid and at most one behaviour marker root.");
            var root = roots.SingleOrDefault();
            if (MainPlayerIntegration.InScene<BirdPoint>(true).Any(point => IsNewType(point.Type) &&
                (root == null || !point.transform.IsChildOf(root.transform))))
                throw new InvalidOperationException("Gull/Pigeon points already exist elsewhere. Review rather than duplicate them.");
            if (root != null && (root.transform.position != Vector3.zero || root.transform.rotation != Quaternion.identity ||
                root.transform.localScale != Vector3.one || root.GetComponents<Component>().Length != 1 ||
                root.transform.Cast<Transform>().Any(child => !specs.Any(spec => spec.name == child.name)) ||
                specs.Any(spec => root.transform.Cast<Transform>().Count(child => child.name == spec.name) > 1) ||
                root.transform.Cast<Transform>().Any(child => child.childCount != 0 ||
                    child.GetComponents<Component>().Any(component => !(component is Transform) && !(component is BirdPoint)) ||
                    child.GetComponents<BirdPoint>().Length > 1)))
                throw new InvalidOperationException("Unexpected marker-root edits. Review before applying the planned layout.");
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place Gull and Pigeon level markers");
            try
            {
                if (root == null)
                {
                    root = new GameObject(RootName);
                    Undo.RegisterCreatedObjectUndo(root, "Add Gull/Pigeon marker root");
                }
                foreach (var spec in specs)
                {
                    var point = root.transform.Find(spec.name);
                    if (point == null)
                    {
                        var item = new GameObject(spec.name);
                        Undo.RegisterCreatedObjectUndo(item, "Add behaviour reference point");
                        item.transform.SetParent(root.transform, false);
                        point = item.transform;
                    }
                    Undo.RecordObject(point, "Apply planned behaviour marker position");
                    point.SetPositionAndRotation(spec.position, Quaternion.identity);
                    point.localScale = Vector3.one;
                    var marker = point.GetComponent<BirdPoint>();
                    if (marker == null) marker = Undo.AddComponent<BirdPoint>(point.gameObject);
                    var data = new SerializedObject(marker);
                    data.FindProperty("type").intValue = (int)spec.type;
                    data.FindProperty("radius").floatValue = spec.radius;
                    data.FindProperty("group").stringValue = spec.group;
                    data.ApplyModifiedProperties();
                }
                Undo.FlushUndoRecordObjects();
                Validate();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save Main.");
                Debug.Log("[MainGullPigeon] Saved one kettle centre, eight FOD points and three roosts. No bird AI or debris spawned.");
            }
            catch { Undo.RevertAllDownToGroup(undoGroup); throw; }
            finally { Undo.CollapseUndoOperations(undoGroup); }
        }

        [MenuItem("Tools/Runway Panic/Validate Main Gull and Pigeon Markers")]
        public static void Validate()
        {
            RequireMain();
            var lines = new List<string> { "Main Gull/Pigeon markers — focused Edit-mode geometry/approach check", "Unity: " + Application.unityVersion };
            int failures = 0;
            Action<bool, string> check = (ok, message) => { lines.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; };
            var root = GameObject.Find(RootName);
            if (root == null) throw new InvalidOperationException("Missing Gull/Pigeon marker root.");
            var all = MainPlayerIntegration.InScene<BirdPoint>(true);
            check(root.transform.childCount == 12 && root.GetComponentsInChildren<BirdPoint>(true).Length == 12 &&
                all.Count(point => point.Type == BirdPointType.KettleCenter) == 1 &&
                all.Count(point => point.Type == BirdPointType.FodSpawn) == 8 && all.Count(point => point.Type == BirdPointType.Roost) == 3,
                "One kettle centre, eight FOD collection points and three roosts, no duplicate types elsewhere");
            check(all.Count(point => point.Type == BirdPointType.Spawn) == 8 && all.Count(point => point.Type == BirdPointType.Perch) == 8 &&
                all.Count(point => point.Type == BirdPointType.Stash) == 2 && all.Count(point => point.Type == BirdPointType.ShelterExit) == 2,
                "Existing airport spawns, Hawk perches and Crow markers retained");
            var grid = MainPlayerIntegration.InScene<FlightGrid>().Single();
            Physics.SyncTransforms(); grid.Build();
            var provider = new GridPathProvider(grid);
            var waypoints = new List<Vector3>();
            foreach (var spec in Specs())
            {
                var point = root.transform.Find(spec.name);
                var marker = point != null ? point.GetComponent<BirdPoint>() : null;
                check(marker != null && marker.isActiveAndEnabled && marker.Type == spec.type && marker.Group == spec.group &&
                    Mathf.Abs(marker.Radius - spec.radius) < .001f && Vector3.Distance(marker.Position, spec.position) < .001f &&
                    point.GetComponents<Component>().All(component => component is Transform || component is BirdPoint ||
                        (spec.type == BirdPointType.FodSpawn && component is FodSpawnPoint)) && point.childCount == 0,
                    spec.name + " has the intended metadata and no collision/rendering at " + spec.position.ToString("F2") + ", radius " + spec.radius);
                if (marker == null || spec.type == BirdPointType.KettleCenter) continue;
                var support = GameObject.Find(spec.support);
                bool supported = OnSurface(marker.Position, support);
                if (spec.type == BirdPointType.Roost)
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4;
                        supported &= OnSurface(marker.Position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * marker.Radius, support);
                    }
                check(supported, spec.name + (spec.type == BirdPointType.Roost ? " centre and eight landing-ring samples fit the intended roof/wing" : " rests on solid airport ground"));
                // Geometry/approach only; live strategies and runtime references are checked separately.
                Vector3 approach = marker.Position + Vector3.up * (spec.type == BirdPointType.Roost ? 1.5f : .8f);
                check(grid.IsFlyable(approach) && provider.TryFindPath(new Vector3(0, 6, 0), approach, null, waypoints),
                    spec.name + " has a clear, reachable prospective flight approach");
            }
            bool kettleClear = grid.IsFlyable(new Vector3(0, 10, 0));
            foreach (float height in new[] { 6f, 10f, 14f })
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4;
                    kettleClear &= grid.IsFlyable(new Vector3(Mathf.Cos(angle) * 8, height, Mathf.Sin(angle) * 8));
                }
            check(kettleClear, "Kettle centre and radius-8 orbit samples at runway-relative heights 6, 10 and 14 m are clear");
            check(root.GetComponentsInChildren<MonoBehaviour>(true).All(component => component is BirdPoint ||
                (component is FodSpawnPoint && component.GetComponent<BirdPoint>()?.Type == BirdPointType.FodSpawn)),
                "Marker group contains only BirdPoint metadata and optional Gull FOD supplies; no brains, spawners or tests");
            lines.Add("Geometry/approach samples only, not live AI, full landing-ring capacity or continuous orbit certification. No Play-mode suite run.");
            lines.Add("Kettle anchor is y=10; orbit heights 6–14 are ABOVE GROUND, not added again to anchor y.");
            lines.Add("Optional Gull supply references are validated separately by Tools > IS > Gull > Validate Main Gull Runtime. Pigeon rally goals are dynamic, not static markers.");
            lines.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/WorldBuilder");
            File.WriteAllLines("Docs/WorldBuilder/GullPigeonMarkerValidation.txt", lines);
            if (failures != 0) throw new InvalidOperationException("Marker placement check failed; see GullPigeonMarkerValidation.txt.");
        }

        private static bool IsNewType(BirdPointType type) => type == BirdPointType.KettleCenter || type == BirdPointType.FodSpawn || type == BirdPointType.Roost;

        private static bool OnSurface(Vector3 point, GameObject support)
        {
            return support != null && Physics.Raycast(point + Vector3.up * .15f, Vector3.down, out var hit, .3f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.transform.IsChildOf(support.transform) && Mathf.Abs(hit.point.y - point.y) < .08f && hit.normal.y > .95f;
        }

        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
        }
    }
}
