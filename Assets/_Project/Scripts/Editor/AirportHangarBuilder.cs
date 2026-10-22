using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Converts only the hangar massing cube; all other level objects remain untouched.</summary>
    public static class AirportHangarBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string PrefabPath = "Assets/_Project/Prefabs/Environment/Greybox/P_Greybox_Hangar.prefab";

        [MenuItem("Tools/Runway Panic/Apply Hangar Wall Signs")]
        private static void ApplyWallSigns()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_Project/Art/Materials/WorldSurfaces/M_RunwayNumbers_Depth.mat");
            if (material == null || material.shader.name != "RunwayPanic/Ground Paint Text")
                throw new InvalidOperationException("The depth-tested world text material is missing.");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var entrance = root.transform.Find("Hangar_Sign");
                var exit = root.transform.Find("Exit_Sign");
                if (entrance == null || exit == null || entrance.GetComponent<TextMesh>() == null || exit.GetComponent<TextMesh>() == null)
                    throw new InvalidOperationException("Expected original hangar sign objects were not found.");
                SetSign(entrance, "HANGAR", new Color32(32, 55, 72, 255), .12f,
                    new Vector3(7.006f, 5.25f, 0), Quaternion.Euler(0, -90, 0));
                SetSign(exit, "EXIT", new Color32(230, 55, 45, 255), .095f,
                    new Vector3(0, 3.75f, 12.094f), Quaternion.identity);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

                void SetSign(Transform sign, string caption, Color color, float size, Vector3 position, Quaternion rotation)
                {
                    var label = sign.GetComponent<TextMesh>();
                    label.text = caption;
                    // The unlit world-text shader consumes vertex RGB in the project's working colour space.
                    label.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
                    label.fontStyle = FontStyle.Bold;
                    label.characterSize = size;
                    sign.localPosition = position;
                    sign.localRotation = rotation;
                    var renderer = sign.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    if (sign.GetComponent<RunwayPanic.World.GroundPaintText>() == null)
                        sign.gameObject.AddComponent<RunwayPanic.World.GroundPaintText>();
                    sign.gameObject.SetActive(true);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[HangarSigns] Applied navy HANGAR on the exterior entrance lintel and red EXIT above the inside exit. Depth tested; no colliders/gameplay changes.");
            var placed = GameObject.Find("Environment/Buildings/Hangar_Greybox/P_Greybox_Hangar");
            if (placed == null) return;
            try
            {
                var t = placed.transform;
                TerminalFlowerDressing.CaptureView(t.TransformPoint(new Vector3(22, 2, 0)),
                    t.TransformPoint(new Vector3(7, 5.25f, 0)), "/private/tmp/RunwayPanic-HangarEntranceSign.png");
                TerminalFlowerDressing.CaptureView(t.TransformPoint(new Vector3(0, 1.8f, 6.5f)),
                    t.TransformPoint(new Vector3(0, 3.75f, 12.094f)), "/private/tmp/RunwayPanic-HangarExitSign.png");
            }
            catch (Exception e) { Debug.LogWarning("[HangarSigns] Preview failed: " + e.Message); }
        }

        [MenuItem("Tools/Runway Panic/Make Hangar Enterable")]
        public static void Build()
        {
            RequireScene();
            var hangar = GameObject.Find("Hangar_Greybox");
            if (hangar == null || hangar.GetComponent<BoxCollider>() == null)
                throw new InvalidOperationException("Expected the original solid Hangar_Greybox cube. Existing enterable buildings will not be overwritten.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                throw new InvalidOperationException("Hangar prefab already exists; edit it normally instead of rebuilding.");
            Physics.SyncTransforms();
            var originalBounds = hangar.GetComponent<BoxCollider>().bounds;
            if (Vector3.Distance(originalBounds.size, new Vector3(14, 6, 25)) > 0.01f)
                throw new InvalidOperationException("Hangar footprint changed; review the dimensions before converting.");
            var concrete = Material("Concrete");
            var metal = Material("Metal");
            var safety = Material("Safety");
            var marking = Material("Marking");

            // Local +X faces the runway; +Z is the secondary escape route.
            var geometry = new GameObject("P_Greybox_Hangar");
            GameObject prefab;
            try
            {
                Cube(geometry, "Floor", new Vector3(0, 0.025f, 0), new Vector3(14, 0.05f, 25), concrete);
                Cube(geometry, "Wall_West", new Vector3(-6.8f, 2.85f, 0), new Vector3(0.4f, 5.7f, 25), concrete);
                Cube(geometry, "Wall_South", new Vector3(0, 2.85f, -12.3f), new Vector3(14, 5.7f, 0.4f), concrete);
                foreach (float z in new[] { -8.75f, 8.75f })
                    Cube(geometry, "Front_Jamb_" + (z < 0 ? "South" : "North"), new Vector3(6.8f, 2.85f, z), new Vector3(0.4f, 5.7f, 7.5f), concrete);
                Cube(geometry, "Front_Lintel", new Vector3(6.8f, 5.25f, 0), new Vector3(0.4f, 0.9f, 10), concrete);
                foreach (float x in new[] { -4.2f, 4.2f })
                    Cube(geometry, "Side_Exit_Jamb_" + (x < 0 ? "West" : "East"), new Vector3(x, 2.85f, 12.3f), new Vector3(5.6f, 5.7f, 0.4f), concrete);
                Cube(geometry, "Side_Exit_Lintel", new Vector3(0, 4.45f, 12.3f), new Vector3(2.8f, 2.5f, 0.4f), concrete);
                Cube(geometry, "Roof", new Vector3(0, 5.85f, 0), new Vector3(14, 0.3f, 25), metal);
                Cube(geometry, "Entrance_Apron", new Vector3(9, 0.016f, 0), new Vector3(4, 0.032f, 12), concrete, false);
                Cube(geometry, "Main_Entry_Stripe", new Vector3(6.8f, 0.056f, 0), new Vector3(0.2f, 0.01f, 9.8f), safety, false);
                Cube(geometry, "Side_Exit_Stripe", new Vector3(0, 0.056f, 12.3f), new Vector3(2.6f, 0.01f, 0.2f), safety, false);
                // Flush floor markings define two maintenance bays without obstructing movement.
                foreach (float z in new[] { -7.7f, 7.7f })
                {
                    foreach (float x in new[] { -5.5f, 3.5f })
                        Cube(geometry, "Bay_Side_Marking", new Vector3(x, 0.056f, z), new Vector3(0.12f, 0.01f, 4.4f), marking, false);
                    foreach (float end in new[] { z - 2.2f, z + 2.2f })
                        Cube(geometry, "Bay_End_Marking", new Vector3(-1, 0.056f, end), new Vector3(9, 0.01f, 0.12f), marking, false);
                }
                Sign(geometry, "Hangar_Sign", "HANGAR", new Vector3(7.015f, 5.25f, 0), new Vector3(0, -90, 0), 0.10f);
                Sign(geometry, "Exit_Sign", "EXIT", new Vector3(0, 3.75f, 12.08f), Vector3.zero, 0.085f);
                foreach (float z in new[] { -7f, 7f })
                {
                    var lamp = new GameObject("Interior_Preview_Light");
                    lamp.transform.SetParent(geometry.transform, false);
                    lamp.transform.localPosition = new Vector3(0, 5.1f, z);
                    var light = lamp.AddComponent<Light>();
                    light.type = LightType.Point; light.range = 15; light.intensity = 3; light.shadows = LightShadows.None;
                }
                // A local marker for future integration, deliberately not a safe-room trigger.
                var centre = new GameObject("Interior_Reference_NO_GAMEPLAY");
                centre.transform.SetParent(geometry.transform, false);
                centre.transform.localPosition = new Vector3(0, 0.12f, 0);
                prefab = PrefabUtility.SaveAsPrefabAsset(geometry, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(geometry); }
            if (prefab == null) throw new InvalidOperationException("Could not save the hangar prefab; original cube is unchanged.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Make hangar enterable");
            Undo.DestroyObjectImmediate(hangar.GetComponent<BoxCollider>());
            Undo.DestroyObjectImmediate(hangar.GetComponent<MeshRenderer>());
            Undo.DestroyObjectImmediate(hangar.GetComponent<MeshFilter>());
            Undo.RecordObject(hangar.transform, "Normalize hangar building root");
            hangar.transform.position = new Vector3(originalBounds.center.x, originalBounds.min.y, originalBounds.center.z);
            hangar.transform.rotation = Quaternion.identity;
            hangar.transform.localScale = Vector3.one;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hangar.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Install enterable hangar shell");
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Validate();
            AirportGreyboxBuilder.Validate();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = hangar;
            Debug.Log("Enterable hangar saved: 10 m runway-facing entrance, 2.8 m side exit, clear roofed interior. No safe-room gameplay added.");
        }

        [MenuItem("Tools/Runway Panic/Validate Enterable Hangar")]
        public static void Validate()
        {
            RequireScene();
            var root = GameObject.Find("Hangar_Greybox");
            if (root == null || root.GetComponent<BoxCollider>() != null)
                throw new InvalidOperationException("Build the enterable hangar before validating.");
            Physics.SyncTransforms();
            var report = new List<string> { "Enterable airport hangar validation", "Unity: " + Application.unityVersion, "Footprint: 14 x 25 m; exterior height: 6 m", "Main opening: 10 x 4.8 m; side exit: 2.8 x 3.2 m", "" };
            int failures = 0;
            Action<bool, string> check = (ok, message) => { report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; };
            check(PrefabUtility.GetNearestPrefabInstanceRoot(root.transform.GetChild(0).gameObject) != null, "Geometry is a reusable prefab instance");
            var colliders = root.GetComponentsInChildren<BoxCollider>();
            foreach (var c in colliders)
            {
                check(c.enabled && !c.isTrigger, "Solid collision: " + c.name);
                Bounds b = c.bounds;
                check(b.min.x > -49.5f && b.max.x < 49.5f && b.min.z > -39.5f && b.max.z < 39.5f, "Inside perimeter: " + c.name);
                foreach (var other in UnityEngine.Object.FindObjectsByType<BoxCollider>())
                {
                    if (other.transform.IsChildOf(root.transform) || other.name == "Ground_100x80" || !other.enabled || other.isTrigger) continue;
                    bool overlaps = Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out float depth);
                    check(!overlaps || depth < 0.005f, c.name + " clear of " + other.name);
                }
            }
            var test = new GameObject("HangarValidationController_TEMP");
            var cc = test.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.25f; cc.skinWidth = 0.035f;
            try
            {
                Action<Vector3> reset = p => { cc.enabled = false; test.transform.position = root.transform.TransformPoint(p); cc.enabled = true; Physics.SyncTransforms(); };
                Action<Vector3, int> move = (d, n) => { for (int i = 0; i < n; i++) cc.Move(d); };
                Func<Vector3> local = () => root.transform.InverseTransformPoint(test.transform.position);
                foreach (float z in new[] { -4.4f, 0f, 4.4f })
                {
                    reset(new Vector3(12, 0.12f, z)); move(new Vector3(-0.1f, -0.02f, 0), 130);
                    check(local().x < 0, "Player enters main opening at z=" + z);
                    move(new Vector3(0.1f, -0.02f, 0), 130);
                    check(local().x > 11.5f, "Player exits main opening at z=" + z);
                }
                reset(new Vector3(0, 0.12f, 15)); move(new Vector3(0, -0.02f, -0.1f), 150);
                check(local().z < 0.5f, "Player enters through side exit");
                move(new Vector3(0, -0.02f, 0.1f), 150);
                check(local().z > 14.5f, "Player leaves through side exit");
                reset(new Vector3(0, 0.12f, 0)); move(new Vector3(-0.1f, -0.02f, 0), 100);
                check(local().x > -6.5f && local().x < -5.5f, "West wall blocks player");
                reset(new Vector3(0, 0.12f, 0)); move(new Vector3(0, -0.02f, -0.1f), 150);
                check(local().z > -12.1f && local().z < -11f, "South wall blocks player");
                reset(new Vector3(0, 2, 0)); move(new Vector3(0, -0.1f, 0), 50);
                check(local().y > 0 && local().y < 0.15f, "Player lands on hangar floor");
                reset(new Vector3(0, 0.12f, 0)); move(new Vector3(0, 0.1f, 0), 80);
                check(local().y < 4f && local().y > 3.5f, "Solid roof blocks upward movement");
                check(Physics.Raycast(root.transform.TransformPoint(new Vector3(0, 0.5f, 0)), Vector3.up, 8), "Roof covers the centre interior");
            }
            finally { UnityEngine.Object.DestroyImmediate(test); }
            var spawns = GameObject.Find("BirdSpawnPoints");
            foreach (Transform spawn in spawns.transform)
                check(!Physics.CheckSphere(spawn.position, 0.5f, ~0, QueryTriggerInteraction.Ignore), "Bird-spawn clearance: " + spawn.name);
            report.Add("No automatic invulnerability, closable doors, AI, or NavMesh added in this geometry step.");
            report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/WorldBuilder");
            File.WriteAllLines("Docs/WorldBuilder/HangarValidation.txt", report);
            if (failures > 0) throw new InvalidOperationException("Hangar validation failed: " + failures + ". See HangarValidation.txt.");
            Debug.Log("Enterable hangar validation passed. Report: Docs/WorldBuilder/HangarValidation.txt");
        }

        private static void RequireScene()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open Main.unity outside Play mode first.");
        }

        private static Material Material(string suffix)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Greybox/M_Greybox_" + suffix + ".mat");
            if (material == null) throw new InvalidOperationException("Missing greybox material: " + suffix);
            return material;
        }

        private static void Cube(GameObject parent, string name, Vector3 p, Vector3 size, Material material, bool collision = true)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent.transform, false);
            cube.transform.localPosition = p; cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        }

        private static void Sign(GameObject parent, string name, string text, Vector3 p, Vector3 angles, float size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent.transform, false);
            obj.transform.localPosition = p; obj.transform.localEulerAngles = angles;
            var mesh = obj.AddComponent<TextMesh>();
            mesh.text = text; mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 64; mesh.characterSize = size; mesh.color = Color.white;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            obj.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;
        }
    }
}
