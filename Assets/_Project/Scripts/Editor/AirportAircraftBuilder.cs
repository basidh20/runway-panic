using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Game-scale aircraft references for later art replacement, not flight gameplay.</summary>
    public static class AirportAircraftBuilder
    {
        private const string Folder = "Assets/_Project/Prefabs/Environment/Greybox/";
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private static readonly string[] Names = { "Plane_A_Greybox", "Plane_B_Greybox", "Plane_C_Greybox", "Plane_D_Greybox" };
        private static readonly Vector3[] Parking = { new Vector3(-20, 0, -25), new Vector3(33.3f, 0, -1), new Vector3(33.3f, 0, 11), new Vector3(38, 0, 23) };

        [MenuItem("Tools/Runway Panic/Refine Plane Greyboxes")]
        public static void Build()
        {
            RequireScene();
            var roots = Names.Select(n => GameObject.Find(n)).ToArray();
            if (roots.Any(r => r == null || r.GetComponent<BoxCollider>() == null || r.transform.childCount != 0))
                throw new InvalidOperationException("Expected all four original cubes. Existing aircraft geometry will not be overwritten.");
            foreach (var name in new[] { "P_Greybox_UtilityAircraft", "P_Greybox_LightAircraft" })
                if (File.Exists(Folder + name + ".prefab") || File.Exists(Folder + name + "_Wing.asset"))
                    throw new InvalidOperationException("Aircraft assets already exist; edit the prefabs normally.");
            var utility = MakePrefab("P_Greybox_UtilityAircraft", 12, 9, 2, 1.65f);
            var light = MakePrefab("P_Greybox_LightAircraft", 9, 8, 1.65f, 1.45f);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine four aircraft greyboxes");
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                Undo.DestroyObjectImmediate(root.GetComponent<BoxCollider>());
                Undo.DestroyObjectImmediate(root.GetComponent<MeshRenderer>());
                Undo.DestroyObjectImmediate(root.GetComponent<MeshFilter>());
                Undo.RecordObject(root.transform, "Set game-scale aircraft parking");
                root.transform.SetPositionAndRotation(Parking[i], Quaternion.Euler(0, i == 0 ? -35 : -90, 0));
                root.transform.localScale = Vector3.one;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(i == 0 ? utility : light, root.transform);
                Undo.RegisterCreatedObjectUndo(instance, "Install aircraft greybox prefab");
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            FinishParking();
            Selection.activeGameObject = GameObject.Find("Aircraft");
            Debug.Log("Four recognisable aircraft greyboxes saved; wing clearance and movement routes validated. No flight or aircraft interactions added.");
        }

        [MenuItem("Tools/Runway Panic/Finish Aircraft Greybox Parking")]
        public static void FinishParking()
        {
            RequireScene();
            var plane = GameObject.Find(Names[3]);
            if (plane == null || plane.GetComponent<BoxCollider>() != null || plane.transform.childCount == 0)
                throw new InvalidOperationException("Refine the aircraft first.");
            if (Vector3.Distance(plane.transform.position, new Vector3(33.3f, 0, 23)) < 0.01f)
            {
                Undo.RecordObject(plane.transform, "Give Plane D terminal-front clearance");
                plane.transform.position = Parking[3];
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
            else if (Vector3.Distance(plane.transform.position, Parking[3]) > 0.01f)
                throw new InvalidOperationException("Plane D has been deliberately repositioned; validate the new layout normally.");
            Validate();
            AirportHangarBuilder.Validate();
            AirportGreyboxBuilder.Validate();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("Aircraft parking validated and Main saved; Plane D nose-side route is clear of the terminal.");
        }

        private static GameObject MakePrefab(string name, float length, float span, float width, float bodyY)
        {
            var body = Material("Concrete"); var dark = Material("Metal");
            var glass = Material("Landmark"); var accent = Material("Safety");
            var root = new GameObject(name);
            try
            {
                var fuselage = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fuselage.name = "Fuselage";
                fuselage.transform.SetParent(root.transform, false);
                fuselage.transform.localPosition = new Vector3(0, bodyY, 0);
                fuselage.transform.localRotation = Quaternion.Euler(90, 0, 0);
                fuselage.transform.localScale = new Vector3(width, length / 2, width);
                fuselage.GetComponent<Renderer>().sharedMaterial = body;
                Cube(root, "Cockpit", new Vector3(0, bodyY + width * 0.36f, length * 0.23f), new Vector3(width * 0.7f, 0.55f, length * 0.15f), glass);
                var wingMesh = WingMesh(span);
                wingMesh.name = name + "_Wing";
                AssetDatabase.CreateAsset(wingMesh, Folder + wingMesh.name + ".asset");
                var wing = new GameObject("Main_Wings");
                wing.transform.SetParent(root.transform, false);
                wing.transform.localPosition = new Vector3(0, 1.25f, 0);
                wing.AddComponent<MeshFilter>().sharedMesh = wingMesh;
                wing.AddComponent<MeshRenderer>().sharedMaterial = body;
                var wingCollider = wing.AddComponent<MeshCollider>();
                wingCollider.sharedMesh = wingMesh; wingCollider.convex = true;
                Cube(root, "Tailplane", new Vector3(0, bodyY + 0.3f, -length * 0.35f), new Vector3(span * 0.38f, 0.18f, 1.3f), body);
                Cube(root, "Tail_Fin", new Vector3(0, bodyY + 1, -length * 0.35f), new Vector3(0.18f, 1.7f, 1.45f), accent);
                foreach (float x in new[] { -width * 0.45f, width * 0.45f })
                {
                    Wheel(root, "Main_Wheel", new Vector3(x, 0.3f, -length * 0.12f), dark);
                    Cube(root, "Main_Gear_Strut", new Vector3(x, 0.75f, -length * 0.12f), new Vector3(0.12f, 0.5f, 0.12f), dark);
                }
                Wheel(root, "Nose_Wheel", new Vector3(0, 0.3f, length * 0.29f), dark);
                Cube(root, "Nose_Gear_Strut", new Vector3(0, 0.75f, length * 0.29f), new Vector3(0.12f, 0.5f, 0.12f), dark);
                foreach (float x in new[] { -span * 0.22f, span * 0.22f })
                    Cube(root, "Engine_Nacelle", new Vector3(x, 1.5f, 0.55f), new Vector3(0.6f, 0.65f, 1.65f), dark);
                return PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Symmetric swept trapezoid, with genuine convex collision rather than a wing-span-sized invisible box.
        private static Mesh WingMesh(float span)
        {
            float half = span * 0.5f;
            var outline = new[] { new Vector2(-half, -0.9f), new Vector2(half, -0.9f), new Vector2(half, 0.35f), new Vector2(0, 1.4f), new Vector2(-half, 0.35f) };
            var vertices = new Vector3[10];
            for (int i = 0; i < 5; i++)
            {
                vertices[i] = new Vector3(outline[i].x, -0.1f, outline[i].y);
                vertices[i + 5] = new Vector3(outline[i].x, 0.1f, outline[i].y);
            }
            var triangles = new List<int>();
            for (int i = 1; i < 4; i++)
            {
                triangles.AddRange(new[] { 0, i, i + 1, 5, i + 6, i + 5 });
            }
            for (int i = 0; i < 5; i++)
            {
                int next = (i + 1) % 5;
                triangles.AddRange(new[] { i, i + 5, next, next, i + 5, next + 5 });
            }
            var mesh = new Mesh { vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static void Wheel(GameObject parent, string name, Vector3 p, Material material)
        {
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = name;
            wheel.transform.SetParent(parent.transform, false);
            wheel.transform.localPosition = p;
            wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            wheel.transform.localScale = new Vector3(0.6f, 0.11f, 0.6f);
            wheel.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(wheel.GetComponent<Collider>());
            wheel.AddComponent<BoxCollider>().size = new Vector3(1, 2, 1);
        }

        [MenuItem("Tools/Runway Panic/Validate Aircraft Greyboxes")]
        public static void Validate()
        {
            RequireScene();
            var roots = Names.Select(n => GameObject.Find(n)).ToArray();
            if (roots.Any(r => r == null || r.GetComponent<BoxCollider>() != null || r.transform.childCount == 0))
                throw new InvalidOperationException("Refine the aircraft cubes before validating.");
            Physics.SyncTransforms();
            var report = new List<string> { "Aircraft greybox validation", "Unity: " + Application.unityVersion, "Game-scale parked props; not real airliner dimensions.", "" };
            int failures = 0;
            Action<bool, string> check = (ok, message) => { report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; };
            var obstacles = UnityEngine.Object.FindObjectsByType<Collider>();
            foreach (var root in roots)
            {
                Bounds bounds = root.GetComponentsInChildren<Renderer>()[0].bounds;
                foreach (var r in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                check(bounds.min.x > -49.5f && bounds.max.x < 49.5f && bounds.min.z > -39.5f && bounds.max.z < 39.5f, root.name + " fits inside the perimeter");
                check(bounds.max.x < -7.5f || bounds.min.x > 7.5f, root.name + " leaves runway clear");
                check(Mathf.Abs(bounds.min.y) < 0.01f, root.name + " wheels rest on the ground");
                check(PrefabUtility.GetNearestPrefabInstanceRoot(root.transform.GetChild(0).gameObject) != null, root.name + " uses a reusable prefab");
                bool clear = true;
                foreach (var c in root.GetComponentsInChildren<Collider>())
                {
                    check(c.enabled && !c.isTrigger, root.name + " solid collision: " + c.name);
                    foreach (var other in obstacles)
                    {
                        if (other.transform.IsChildOf(root.transform) || other.name == "Ground_100x80" || !other.enabled || other.isTrigger || other is CharacterController) continue;
                        bool hit = Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                            other, other.transform.position, other.transform.rotation, out _, out float depth);
                        if (hit && depth > 0.005f) { clear = false; report.Add("OVERLAP: " + root.name + "/" + c.name + " with " + other.name); }
                    }
                }
                check(clear, root.name + " is clear of other aircraft, buildings, and cover");
                report.Add(root.name + " origin=" + root.transform.position.ToString("F2") + " yaw=" + root.transform.eulerAngles.y.ToString("F1") + " world bounds=" + bounds.size.ToString("F2"));
            }
            var test = new GameObject("AircraftValidationController_TEMP");
            var cc = test.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.25f; cc.skinWidth = 0.035f;
            var preview = GameObject.Find("Player_Preview")?.GetComponent<CharacterController>();
            bool previewEnabled = preview != null && preview.enabled;
            // The parked gameplay player otherwise blocks the simulated runway walk.
            var player = GameObject.Find("P_Player_Gameplay")?.GetComponent<CharacterController>();
            bool playerEnabled = player != null && player.enabled;
            if (preview != null) preview.enabled = false;
            if (player != null) player.enabled = false;
            try
            {
                Action<Vector3> reset = p => { cc.enabled = false; test.transform.position = p; cc.enabled = true; Physics.SyncTransforms(); };
                Action<Vector3, int> move = (d, n) => { for (int i = 0; i < n; i++) cc.Move(d); };
                foreach (var root in roots)
                {
                    float span = root == roots[0] ? 11 : 8;
                    reset(root.transform.TransformPoint(new Vector3(span * 0.5f + 1, 0.12f, 0)));
                    move(root.transform.TransformDirection(new Vector3(-0.1f, -0.02f, 0)), 120);
                    check(root.transform.InverseTransformPoint(test.transform.position).x > 0, root.name + " wing blocks the human-sized player");
                    float front = root == roots[0] ? 8 : 6.5f;
                    reset(root.transform.TransformPoint(new Vector3(-span * 0.5f - 1, 0.12f, front)));
                    move(root.transform.TransformDirection(new Vector3(0.1f, -0.02f, 0)), (int)((span + 2) * 10));
                    check(root.transform.InverseTransformPoint(test.transform.position).x > span * 0.5f, root.name + " can be walked around at the nose");
                }
                foreach (float z in new[] { 5f, 17f })
                {
                    reset(new Vector3(24, 0.12f, z)); move(new Vector3(0.1f, -0.02f, 0), 220);
                    check(test.transform.position.x > 45.5f, "Clear east parking aisle at z=" + z);
                }
                reset(new Vector3(0, 0.12f, -35)); move(new Vector3(0, -0.02f, 0.1f), 700);
                check(test.transform.position.z > 34.5f, "Runway remains traversable end to end");
                if (test.transform.position.z <= 34.5f)
                {
                    report.Add("Runway test stopped at " + test.transform.position.ToString("F2"));
                    foreach (var hit in Physics.CapsuleCastAll(new Vector3(0, 0.55f, -35), new Vector3(0, 1.45f, -35),
                        0.3f, Vector3.forward, 70f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider != cc && !(hit.collider is CharacterController))
                            report.Add("Runway test path collider: " + hit.collider.name + " at distance " + hit.distance.ToString("F2"));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(test); if (preview != null) preview.enabled = previewEnabled; if (player != null) player.enabled = playerEnabled; }
            foreach (Transform spawn in GameObject.Find("BirdSpawnPoints").transform)
                check(!Physics.CheckSphere(spawn.position, 0.5f, ~0, QueryTriggerInteraction.Ignore), "Bird clearance: " + spawn.name);
            report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/WorldBuilder");
            File.WriteAllLines("Docs/WorldBuilder/AircraftValidation.txt", report);
            if (failures > 0) throw new InvalidOperationException("Aircraft validation failed: " + failures + ". See AircraftValidation.txt.");
            Debug.Log("Aircraft greybox validation passed. Report: Docs/WorldBuilder/AircraftValidation.txt");
        }

        private static void RequireScene()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open Main.unity outside Play mode.");
        }

        private static Material Material(string name)
        {
            var result = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Greybox/M_Greybox_" + name + ".mat");
            if (result == null) throw new InvalidOperationException("Missing material: " + name);
            return result;
        }

        private static void Cube(GameObject parent, string name, Vector3 p, Vector3 size, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name; obj.transform.SetParent(parent.transform, false);
            obj.transform.localPosition = p; obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
