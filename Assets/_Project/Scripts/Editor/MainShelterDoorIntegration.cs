using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Places S2's existing doors; the opt-in smoke check never changes the saved scene.</summary>
    [InitializeOnLoad]
    public static class MainShelterDoorIntegration
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interactables/P_Door_Shelter.prefab";
        private const string ReportPath = "Docs/WorldBuilder/ShelterDoorValidation.txt";
        private const string SessionKey = "RunwayPanic.ShelterDoorSmoke";
        private static readonly string[] Shelters = { "Shelter_A", "Shelter_B" };
        private static readonly List<string> Report = new List<string>();
        private static Door[] doors;
        private static Vector3[] forwards, positions;
        private static int phase, failures;
        private static double phaseStarted;
        private static bool running;
        private static float closingOvershoot;

        static MainShelterDoorIntegration() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Tools/Runway Panic/Integrate Main Shelter Doors")]
        public static void Integrate()
        {
            RequireMain();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var shelters = Shelters.Select(name => GameObject.Find("Environment/Buildings/" + name)).ToArray();
            if (prefab == null || shelters.Any(shelter => shelter == null))
                throw new InvalidOperationException("Expected the shared door prefab and both original shelters.");
            foreach (var shelter in shelters)
            {
                var existing = shelter.transform.Find("P_Door_Shelter");
                if (shelter.GetComponentsInChildren<Door>(true).Length > (existing == null ? 0 : 1) ||
                    (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) != prefab))
                    throw new InvalidOperationException("Unexpected door in " + shelter.name + "; review before replacing anything.");
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install shelter doors");
            try
            {
                foreach (var shelter in shelters)
                {
                    var root = shelter.transform.Find("P_Door_Shelter");
                    if (root == null)
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, shelter.transform);
                        Undo.RegisterCreatedObjectUndo(instance, "Add shared shelter door");
                        root = instance.transform;
                    }
                    Undo.RecordObject(root, "Fit door to shelter opening");
                    root.localPosition = new Vector3(0, 0.055f, -3.8f);
                    root.localRotation = Quaternion.Euler(0, 180, 0);
                    root.localScale = Vector3.one;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                    ConfigureDoor(root);
                    // Scene-instance overrides only: retain shelter footprint and shared prefab assets.
                    Resize(shelter.transform.Find("Front_Left"), new Vector3(-2.875f, 2, -3.8f), new Vector3(4.25f, 4, .4f));
                    Resize(shelter.transform.Find("Front_Right"), new Vector3(2.875f, 2, -3.8f), new Vector3(4.25f, 4, .4f));
                    Resize(shelter.transform.Find("Door_Lintel"), new Vector3(0, 3.3f, -3.8f), new Vector3(1.5f, 1.4f, .4f));
                    var stripe = shelter.transform.Find("EntryStripe");
                    Resize(stripe, stripe.localPosition, new Vector3(1.3f, .01f, .2f));
                }
                Undo.FlushUndoRecordObjects();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save Main.");
                Debug.Log("[ShelterDoors] Installed two connected S2 door prefabs; use E while aiming at a door.");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        private static void ConfigureDoor(Transform root)
        {
            var panel = root.Find("DoorPanel");
            Resize(panel.Find("Panel_Mesh"), new Vector3(0, 1.28f, 0), new Vector3(1.4f, 2.4f, .08f));
            Resize(panel.Find("Kickplate_Stripe"), new Vector3(0, .28f, 0), new Vector3(1.38f, .12f, .09f));
            foreach (string handle in new[] { "Handle_Inside", "Handle_Outside" })
            {
                var part = panel.Find(handle);
                Resize(part, new Vector3(.52f, 1.08f, part.localPosition.z), part.localScale);
            }
            var box = panel.GetComponent<BoxCollider>();
            Undo.RecordObject(box, "Match narrow door collision");
            box.size = new Vector3(1.4f, 2.4f, .08f);
            box.center = new Vector3(0, 1.28f, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(box);
            var hinge = panel.GetComponent<HingeJoint>();
            Undo.RecordObject(hinge, "Move hinge to resized panel edge");
            hinge.anchor = new Vector3(-.7f, 0, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hinge);
            var body = panel.GetComponent<Rigidbody>();
            Undo.RecordObject(body, "Tune door inertia");
            body.mass = 20;
            body.angularDamping = 1;
            PrefabUtility.RecordPrefabInstancePropertyModifications(body);
            // Door.Awake applies these serialized settings to the joint; overriding the joint spring alone would be lost.
            var settings = new SerializedObject(panel.GetComponent<Door>());
            settings.FindProperty("spring").floatValue = 1000;
            settings.FindProperty("damper").floatValue = 240;
            settings.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(settings.targetObject);
        }

        private static void Resize(Transform part, Vector3 position, Vector3 size)
        {
            if (part == null) throw new InvalidOperationException("Expected shared prefab geometry is missing.");
            Undo.RecordObject(part, "Fit narrow shelter entrance");
            part.localPosition = position;
            part.localScale = size;
            PrefabUtility.RecordPrefabInstancePropertyModifications(part);
        }

        [MenuItem("Tools/Runway Panic/Run Main Shelter Door Smoke Check")]
        public static void RunSmokeCheck()
        {
            RequireMain();
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save Main first.");
            SessionState.SetBool(SessionKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                phase = failures = 0;
                closingOvershoot = 0;
                running = true;
                phaseStarted = EditorApplication.timeSinceStartup;
                Report.Clear();
                Report.Add("Main shelter doors — focused placement / physical hinge / doorway clearance smoke check");
                Report.Add("Unity: " + Application.unityVersion);
                Report.Add("Uses shared Door.Open/Close APIs, not simulated E input. No full-game regression run.");
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && running)
            {
                Check(false, "Smoke check interrupted");
                Finish(false);
            }
            else if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(SessionKey, false);
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (doors != null && (phase == 2 || phase == 4))
                foreach (var door in doors)
                    closingOvershoot = Mathf.Max(closingOvershoot, phase == 2 ? -door.CurrentAngle : door.CurrentAngle);
            if (EditorApplication.timeSinceStartup - phaseStarted < 1.5) return;
            try
            {
                if (phase == 0)
                {
                    doors = Shelters.Select(name => GameObject.Find("Environment/Buildings/" + name + "/P_Door_Shelter")
                        ?.GetComponentInChildren<Door>()).ToArray();
                    Check(doors.All(door => door != null) && MainPlayerIntegration.InScene<Door>(true).Length == 2,
                        "Exactly one shared Door in each shelter, two total");
                    if (doors.Any(door => door == null)) { Finish(); return; }
                    forwards = doors.Select(door => door.transform.forward).ToArray();
                    positions = doors.Select(door => door.transform.position).ToArray();
                    for (int i = 0; i < doors.Length; i++)
                    {
                        var root = doors[i].transform.parent;
                        Check(Vector3.Distance(root.localPosition, new Vector3(0, .055f, -3.8f)) < .001f &&
                            Quaternion.Angle(root.localRotation, Quaternion.Euler(0, 180, 0)) < .1f && root.localScale == Vector3.one,
                            Shelters[i] + " handoff placement and scale");
                        Check(doors[i].GetComponent<BoxCollider>().size == new Vector3(1.4f, 2.4f, .08f),
                            Shelters[i] + " panel/collider resized to 1.4 x 2.4 m");
                        Check(!doors[i].IsOpen && !doors[i].IsLocked && !PassageClear(i), Shelters[i] + " closed door blocks passage");
                        doors[i].Open(positions[i] + forwards[i] * 2);
                    }
                }
                else
                {
                    for (int i = 0; i < doors.Length; i++)
                    {
                        float expected = phase == 1 ? 90 : phase == 3 ? -90 : 0;
                        Check(Mathf.Abs(Mathf.DeltaAngle(doors[i].CurrentAngle, expected)) < 5,
                            Shelters[i] + " physical hinge reaches " + expected + " degrees (actual " + doors[i].CurrentAngle.ToString("F1") + ")");
                        Check(PassageClear(i) == (phase == 1 || phase == 3),
                            Shelters[i] + (phase == 1 || phase == 3 ? " open doorway has human-sized clearance" : " closed door blocks passage again"));
                        if (phase == 1 || phase == 3) doors[i].Close();
                        else if (phase == 2) doors[i].Open(positions[i] - forwards[i] * 2);
                    }
                    if (phase == 4)
                    {
                        Check(closingOvershoot < 2, "Closing overshoot stays below 2 degrees (actual " + closingOvershoot.ToString("F2") + ")");
                        Finish(); return;
                    }
                }
                phase++;
                phaseStarted = EditorApplication.timeSinceStartup;
            }
            catch (Exception exception) { Check(false, exception.ToString()); Finish(); }
        }

        private static bool PassageClear(int index)
        {
            // A 0.7 m wide, 1.8 m tall capsule crosses the centre of the doorway, above the floor.
            Physics.SyncTransforms();
            Vector3 start = positions[index] + forwards[index] * 1.8f + Vector3.up * .4f;
            return !Physics.CapsuleCast(start, start + Vector3.up * 1.1f, .35f, -forwards[index],
                out _, 3.6f, ~0, QueryTriggerInteraction.Ignore);
        }

        private static void Check(bool ok, string message)
        {
            Report.Add((ok ? "PASS: " : "FAIL: ") + message);
            if (!ok) failures++;
        }

        private static void Finish(bool exitPlay = true)
        {
            running = false;
            EditorApplication.update -= Tick;
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllLines(ReportPath, Report);
            Debug.Log("[ShelterDoors] Smoke check finished. Failures: " + failures + "; " + ReportPath);
            if (exitPlay) EditorApplication.isPlaying = false;
        }

        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
        }
    }
}
