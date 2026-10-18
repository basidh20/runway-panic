using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Agents;
using RunwayPanic.Agents.Crow;
using RunwayPanic.AI;
using RunwayPanic.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Installs the Crow owner's delivered level setup, without copying its markers or changing its logic.</summary>
    public static class MainCrowIntegration
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Birds/Crow/P_CrowSetup_Main.prefab";
        private const string RootName = "P_CrowSetup_Main";

        [MenuItem("Tools/Runway Panic/Integrate Main Crow Setup")]
        public static void Integrate()
        {
            RequireMain();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var roots = SceneManager.GetActiveScene().GetRootGameObjects().Where(root => root.name == RootName).ToArray();
            if (prefab == null || roots.Length > 1 ||
                (roots.Length == 1 && PrefabUtility.GetCorrespondingObjectFromSource(roots[0]) != prefab) ||
                (roots.Length == 0 && (MainPlayerIntegration.InScene<ShelterZone>(true).Length != 0 ||
                    MainPlayerIntegration.InScene<BirdPoint>(true).Any(point => point.Type == BirdPointType.Stash || point.Type == BirdPointType.ShelterExit))))
                throw new InvalidOperationException("Missing shared prefab or unexpected existing Crow setup/markers. Review before duplicating anything.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install supplied Crow setup in Main");
            try
            {
                var root = roots.SingleOrDefault();
                if (root == null)
                {
                    root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
                    Undo.RegisterCreatedObjectUndo(root, "Add shared Crow setup");
                }
                Undo.RecordObject(root.transform, "Keep Crow setup at the world origin");
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
                Undo.FlushUndoRecordObjects();
                Validate();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save Main.");
                Debug.Log("[MainCrow] Saved the connected Crow setup: two stashes, two shelter zones and two exits. No Crow spawned.");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        [MenuItem("Tools/Runway Panic/Validate Main Crow Setup")]
        public static void Validate()
        {
            RequireMain();
            var lines = new List<string> { "Main Crow setup — focused Edit-mode placement/trigger-group/approach check", "Unity: " + Application.unityVersion };
            int failures = 0;
            Action<bool, string> check = (ok, message) => { lines.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; };
            var root = GameObject.Find(RootName);
            if (root == null) throw new InvalidOperationException("Expected the connected Crow setup in Main.");
            check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == PrefabPath && root.transform.position == Vector3.zero &&
                Quaternion.Angle(root.transform.rotation, Quaternion.identity) < .01f && root.transform.localScale == Vector3.one,
                "One connected shared setup at origin, identity rotation and unit scale");
            var points = root.GetComponentsInChildren<BirdPoint>(true);
            var stashes = points.Where(point => point.Type == BirdPointType.Stash).ToArray();
            var exits = points.Where(point => point.Type == BirdPointType.ShelterExit).ToArray();
            var zones = root.GetComponentsInChildren<ShelterZone>(true);
            check(points.Length == 4 && stashes.Length == 2 && exits.Length == 2, "Shared prefab supplies exactly two stashes and two exits");
            check(zones.Length == 2 && MainPlayerIntegration.InScene<ShelterZone>(true).Length == 2, "Two shelter zones, no scene duplicates");
            var grid = MainPlayerIntegration.InScene<FlightGrid>().Single();
            check(MainPlayerIntegration.InScene<PickupSpawner>().Length == 1, "Existing pickup spawner retained, not duplicated");
            check(MainPlayerIntegration.InScene<BirdPoint>().Count(point => point.Type == BirdPointType.Spawn) == 8 &&
                MainPlayerIntegration.InScene<BirdPoint>().Count(point => point.Type == BirdPointType.Perch) == 8, "Eight airport spawns and eight Hawk perches retained");
            Physics.SyncTransforms(); grid.Build();
            var provider = new GridPathProvider(grid);
            var path = new List<Vector3>();
            foreach (var stash in stashes)
            {
                check(stash.isActiveAndEnabled && GroundAndBodyClear(stash.Position), stash.name + " is on solid ground with human-sized local clearance at " + stash.Position.ToString("F2"));
                // Matches CrowBrain.Theft's 0.8 m pickup/stash approach height.
                Vector3 approach = stash.Position + Vector3.up * .8f;
                check(grid.IsFlyable(approach) && provider.TryFindPath(new Vector3(0, 6, 0), approach, null, path),
                    stash.name + " has a grid route to its actual Crow approach height");
            }
            foreach (var zone in zones)
            {
                string shelterName = zone.Group == "ShelterA" ? "Shelter_A" : zone.Group == "ShelterB" ? "Shelter_B" : "";
                var shelter = GameObject.Find("Environment/Buildings/" + shelterName);
                var box = zone.GetComponent<BoxCollider>();
                var exit = exits.SingleOrDefault(point => point.Group == zone.Group);
                check(zone.isActiveAndEnabled && box != null && box.enabled && box.isTrigger && shelter != null &&
                    Vector3.Distance(zone.Centre, shelter.transform.TransformPoint(new Vector3(0, 1.55f, 0))) < .01f &&
                    Quaternion.Angle(zone.transform.rotation, shelter.transform.rotation) < .1f && box.size == new Vector3(9.2f, 2.9f, 7.2f),
                    zone.Group + " trigger matches the shelter's interior position, dimensions and rotation");
                check(exit != null && exit.isActiveAndEnabled && GroundAndBodyClear(exit.Position), zone.Group + " has one matching ground-level exit in clear space");
                if (exit == null || shelter == null) continue;
                var door = shelter.transform.Find("P_Door_Shelter");
                check(door != null && Vector3.Dot(exit.Position - door.position, door.forward) > 1 &&
                    Vector3.Distance(box.ClosestPoint(exit.Position + Vector3.up), exit.Position + Vector3.up) > .5f,
                    zone.Group + " guard point is outside the fitted doorway and zone, not inside the room");
                Vector3 aboveExit = exit.Position + Vector3.up * 2; // CrowBrain.Vigil's approach height.
                check(grid.IsFlyable(aboveExit) && provider.TryFindPath(new Vector3(0, 6, 0), aboveExit, null, path),
                    zone.Group + " exit has an outdoor flight approach route");
            }
            check(root.GetComponentsInChildren<CrowBrain>(true).Length == 0, "Setup prefab adds no live Crow or test spawner");
            lines.Add("Geometry/asset check only: no Crow spawned, no trigger entry/exit simulation or theft/vigil AI suite run.");
            lines.Add("Shelter zones detect occupancy only; no invulnerability, stay timer or cooldown added.");
            lines.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/WorldBuilder");
            File.WriteAllLines("Docs/WorldBuilder/CrowSetupValidation.txt", lines);
            if (failures != 0) throw new InvalidOperationException("Crow setup check failed; see CrowSetupValidation.txt.");
        }

        private static bool GroundAndBodyClear(Vector3 point)
        {
            if (!Physics.Raycast(point + Vector3.up * .4f, Vector3.down, out var hit, 1, ~0, QueryTriggerInteraction.Ignore) ||
                Mathf.Abs(point.y - hit.point.y) > .1f || hit.normal.y < .95f) return false;
            return !Physics.CheckCapsule(hit.point + Vector3.up * .36f, hit.point + Vector3.up * 1.44f, .35f, ~0, QueryTriggerInteraction.Ignore);
        }

        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
        }
    }
}
