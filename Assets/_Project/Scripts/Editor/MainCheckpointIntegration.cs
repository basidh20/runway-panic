using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Connects the existing level markers to S2's checkpoint/respawn system; no duplicate runtime logic.</summary>
    public static class MainCheckpointIntegration
    {
        public const string ReportPath = "Docs/WorldBuilder/CheckpointValidation.txt";
        public static readonly string[] MarkerNames = { "Checkpoint_A", "Checkpoint_B" };
        private const string MarkerRoot = "GameplayMarkers/Checkpoints/";
        private const string MarkingName = "CheckpointMarking";
        private static readonly Vector3 TriggerSize = new Vector3(4, 3, 4);

        [MenuItem("Tools/Runway Panic/Integrate Main Checkpoints")]
        public static void Integrate()
        {
            RequireMain();
            var markers = MarkerNames.Select(FindMarker).ToArray();
            if (markers.Any(marker => marker == null))
                throw new InvalidOperationException("Expected both original Checkpoint_A/B markers. No scene changes made.");
            foreach (var marker in markers)
            {
                if (marker.GetComponents<Collider>().Any(collider => !(collider is BoxCollider)) ||
                    marker.GetComponents<BoxCollider>().Length > 1 || marker.GetComponents<Checkpoint>().Length > 1)
                    throw new InvalidOperationException("Unexpected/duplicate components on " + marker.name + ". Review before integration.");
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Greybox/M_Greybox_Safety.mat");
            if (material == null) throw new InvalidOperationException("Expected the shared greybox marking material.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Activate Main checkpoints");
            try
            {
                foreach (var marker in markers)
                {
                    var box = marker.GetComponent<BoxCollider>();
                    if (box == null) box = Undo.AddComponent<BoxCollider>(marker);
                    Undo.RecordObject(box, "Configure checkpoint trigger");
                    box.isTrigger = true;
                    box.center = new Vector3(0, 1.5f, 0);
                    box.size = TriggerSize;
                    if (marker.GetComponent<Checkpoint>() == null) Undo.AddComponent<Checkpoint>(marker);
                    Undo.RecordObject(marker.transform, "Face respawn toward the runway");
                    marker.transform.rotation = Quaternion.Euler(0, marker.name == "Checkpoint_A" ? 90 : -90, 0);
                    if (marker.transform.Find(MarkingName) == null) AddFloorMarking(marker.transform, material);
                    var label = marker.transform.Find(MarkingName + "/CheckpointLabel");
                    if (label != null)
                    {
                        Undo.RecordObject(label, "Keep checkpoint text clear of the existing shelter label");
                        label.localPosition = new Vector3(0, -0.055f, 1);
                    }
                }
                Physics.SyncTransforms();
                Validate();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save Main.");
                Debug.Log("[MainCheckpoints] Both original markers now activate S2 respawns; runway-facing floor markings added.");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        [MenuItem("Tools/Runway Panic/Validate Main Checkpoints")]
        public static void Validate()
        {
            RequireMain();
            Physics.SyncTransforms();
            var lines = new List<string> { "Main checkpoint integration — saved-scene checks", "Unity: " + Application.unityVersion };
            int failed = 0;
            Action<bool, string> check = (ok, text) => { lines.Add((ok ? "PASS: " : "FAIL: ") + text); if (!ok) failed++; };
            check(MainPlayerIntegration.InScene<Checkpoint>(true).Length == 2, "Exactly two checkpoint components in Main");
            foreach (string name in MarkerNames)
            {
                var marker = FindMarker(name);
                check(marker != null, name + " original marker retained");
                if (marker == null) continue;
                Vector3 expected = name == "Checkpoint_A" ? new Vector3(-13, 0.12f, 12) : new Vector3(13, 0.12f, -12);
                check(Vector3.Distance(marker.transform.position, expected) < 0.001f, name + " original ground position preserved");
                check(marker.activeInHierarchy && marker.GetComponent<Checkpoint>() != null && marker.GetComponent<Checkpoint>().enabled,
                    name + " uses the active shared S2 Checkpoint component");
                var box = marker.GetComponent<BoxCollider>();
                check(box != null && box.enabled && box.isTrigger && box.size == TriggerSize && box.center == new Vector3(0, 1.5f, 0),
                    name + " has a 4 x 3 x 4 m trigger above the floor");
                Vector3 runwayDirection = new Vector3(-marker.transform.position.x, 0, 0).normalized;
                check(Vector3.Dot(marker.transform.forward, runwayDirection) > 0.99f, name + " respawns facing the runway");
                check(Physics.Raycast(marker.transform.position + Vector3.up * 0.2f, Vector3.down, 1, ~0, QueryTriggerInteraction.Ignore),
                    name + " has solid ground underneath");
                Vector3 lower = marker.transform.position + Vector3.up * 0.35f;
                check(!Physics.CheckCapsule(lower, lower + Vector3.up * 1.1f, 0.35f, ~0, QueryTriggerInteraction.Ignore),
                    name + " human-sized respawn volume is clear of solid obstacles");
                var marking = marker.transform.Find(MarkingName);
                check(marking != null && marking.GetComponentsInChildren<Collider>(true).Length == 0,
                    name + " visible floor marking adds no blocking colliders");
            }
            var player = MainPlayerIntegration.InScene<PlayerRespawn>();
            check(player.Length == 1, "One existing gameplay PlayerRespawn will receive checkpoint triggers");
            lines.Add("Uses S2 runtime code unchanged. No safe-room protection, doors, checkpoint HUD or cross-session saving added.");
            lines.Add("Failures: " + failed);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllLines(ReportPath, lines);
            if (failed != 0) throw new InvalidOperationException("Checkpoint validation failed; see " + ReportPath);
        }

        public static GameObject FindMarker(string name) => GameObject.Find(MarkerRoot + name);

        private static void AddFloorMarking(Transform marker, Material material)
        {
            var root = new GameObject(MarkingName);
            Undo.RegisterCreatedObjectUndo(root, "Add checkpoint marking");
            root.transform.SetParent(marker, false);
            // Marker origin is y=.12; these non-colliding strips sit just above the existing apron.
            foreach (float x in new[] { -1.9f, 1.9f })
                foreach (float z in new[] { -1.9f, 1.9f })
                {
                    Stripe(root.transform, material, new Vector3(x, -0.065f, z - Mathf.Sign(z) * 0.45f), new Vector3(0.08f, 0.008f, 0.9f));
                    Stripe(root.transform, material, new Vector3(x - Mathf.Sign(x) * 0.45f, -0.065f, z), new Vector3(0.9f, 0.008f, 0.08f));
                }
            var label = new GameObject("CheckpointLabel");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0, -0.055f, 1);
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var text = label.AddComponent<TextMesh>();
            text.text = "CHECKPOINT " + (marker.name == "Checkpoint_A" ? "A" : "B");
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 64;
            text.characterSize = 0.08f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        }

        private static void Stripe(Transform parent, Material material, Vector3 position, Vector3 size)
        {
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "CornerStripe";
            stripe.transform.SetParent(parent, false);
            stripe.transform.localPosition = position;
            stripe.transform.localScale = size;
            UnityEngine.Object.DestroyImmediate(stripe.GetComponent<Collider>());
            var renderer = stripe.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open saved Main outside Play mode before configuring checkpoints.");
        }
    }
}
