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
    /// <summary>Small, undoable corrections to the student's additions; never rebuilds the scene.</summary>
    public static class AirportGreyboxReview
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("Tools/Runway Panic/Adjust User Greybox Additions")]
        public static void Adjust()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open Main.unity outside Play mode first.");
            var environment = Required("Environment");
            var ground = Required("Ground_100x80").GetComponent<BoxCollider>();
            var runway = Required("Runway");
            var surface = runway.Cast<Transform>().Single(t => t.name.StartsWith("Runway_") && t.GetComponent<MeshRenderer>() != null);
            var planes = new[] { "A", "B", "C", "D" }.Select(id => Required("Plane_" + id + "_Greybox")).ToArray();
            var hangar = GameObject.Find("Hangar_Greybox") ?? GameObject.Find("Hanger_Greybox");
            if (hangar == null || ground == null) throw new InvalidOperationException("Ground or hangar missing; nothing adjusted.");
            if (hangar.GetComponent<BoxCollider>() == null)
                throw new InvalidOperationException("Hangar has already been converted. Use Validate Enterable Hangar; this one-off cube review no longer applies.");
            var stripes = runway.Cast<Transform>().Where(t => t.name.StartsWith("CentreStripe_")).OrderBy(t => t.localPosition.z).ToArray();
            if (stripes.Length < 2) throw new InvalidOperationException("Runway centre stripes missing; nothing adjusted.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Review airport greybox additions");
            var aircraft = environment.Find("Aircraft");
            if (aircraft == null)
            {
                var obj = new GameObject("Aircraft");
                Undo.RegisterCreatedObjectUndo(obj, "Group plane placeholders");
                Undo.SetTransformParent(obj.transform, environment, "Group plane placeholders");
                aircraft = obj.transform;
            }
            Physics.SyncTransforms();
            foreach (var plane in planes)
            {
                var collider = plane.GetComponent<BoxCollider>();
                if (collider == null) throw new InvalidOperationException(plane.name + " needs a BoxCollider.");
                Undo.RecordObject(plane, "Ground plane placeholder");
                plane.position += Vector3.up * (ground.bounds.max.y - collider.bounds.min.y);
                Undo.SetTransformParent(plane, aircraft, "Group plane placeholders");
            }
            Undo.RecordObject(hangar, "Correct hangar spelling");
            hangar.name = "Hangar_Greybox";
            Undo.SetTransformParent(hangar.transform, Required("Buildings"), "Group hangar with buildings");
            var markerParent = Required("BirdSpawnPoints").parent;
            if (markerParent.name == "Cube" && markerParent.GetComponent<Renderer>() == null && markerParent.GetComponent<Collider>() == null)
            {
                Undo.RecordObject(markerParent.gameObject, "Restore marker group name");
                markerParent.name = "GameplayMarkers";
            }

            // Keep the student's 75 m surface and all added stripes. Reserve clear areas for numbers and thresholds.
            float halfLength = surface.localScale.z * 0.5f;
            Undo.RecordObject(surface.gameObject, "Update runway dimension label");
            surface.name = "Runway_" + surface.localScale.z.ToString("0.#") + "x" + surface.localScale.x.ToString("0.#");
            foreach (Transform mark in runway)
            {
                if (!mark.name.StartsWith("Threshold_") && !mark.name.StartsWith("RunwayNumber_")) continue;
                Undo.RecordObject(mark, "Align longer runway markings");
                Vector3 p = mark.localPosition;
                p.z = Mathf.Sign(p.z) * (halfLength - (mark.name.StartsWith("Threshold_") ? 3f : 8f));
                mark.localPosition = p;
            }
            float stripeExtent = halfLength - 13.5f;
            float spacing = stripeExtent * 2 / (stripes.Length - 1);
            for (int i = 0; i < stripes.Length; i++)
            {
                Undo.RecordObject(stripes[i], "Space centre stripes between runway numbers");
                Vector3 p = stripes[i].localPosition;
                p.z = -stripeExtent + spacing * i;
                stripes[i].localPosition = p;
                Vector3 scale = stripes[i].localScale;
                scale.z = Mathf.Min(3f, spacing * 0.525f);
                stripes[i].localScale = scale;
            }
            var bird = Required("BirdSpawn_05");
            if (hangar.GetComponent<BoxCollider>().bounds.SqrDistance(bird.position) < 1f)
            {
                Undo.RecordObject(bird, "Clear bird spawn from hangar");
                bird.position = new Vector3(-47, 8, 0);
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Physics.SyncTransforms();
            ValidateAdditions(planes, hangar.GetComponent<BoxCollider>(), ground, surface);
            AirportGreyboxBuilder.Validate();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Selection.activeGameObject = aircraft.gameObject;
            Debug.Log("User greybox additions adjusted and saved. Original plane footprints and hangar placement preserved.");
        }

        private static Transform Required(string name)
        {
            var obj = GameObject.Find(name);
            if (obj == null) throw new InvalidOperationException("Missing scene object: " + name);
            return obj.transform;
        }

        private static void ValidateAdditions(Transform[] planes, BoxCollider hangar, BoxCollider ground, Transform surface)
        {
            int failures = 0;
            var report = new List<string> { "Student greybox additions review", "Unity: " + Application.unityVersion, "" };
            Action<bool, string> check = (ok, message) => { report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; };
            var obstacles = UnityEngine.Object.FindObjectsByType<BoxCollider>();
            foreach (var transform in planes.Concat(new[] { hangar.transform }))
            {
                var collider = transform.GetComponent<BoxCollider>();
                check(collider.enabled && !collider.isTrigger, transform.name + " has solid collision");
                check(Mathf.Abs(collider.bounds.min.y - ground.bounds.max.y) < 0.005f, transform.name + " rests on the ground");
                var b = collider.bounds;
                check(b.min.x > -49.5f && b.max.x < 49.5f && b.min.z > -39.5f && b.max.z < 39.5f, transform.name + " fits inside the perimeter");
                foreach (var other in obstacles)
                {
                    if (other == collider || other == ground || !other.enabled || other.isTrigger) continue;
                    bool overlap = Physics.ComputePenetration(collider, transform.position, transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out float depth);
                    check(!overlap || depth < 0.005f, transform.name + " does not overlap " + other.name);
                }
                report.Add("Transform: " + transform.name + " position=" + transform.position.ToString("F3") + " scale=" + transform.lossyScale.ToString("F3"));
            }
            foreach (Transform spawn in Required("BirdSpawnPoints"))
                check(!Physics.CheckSphere(spawn.position, 0.5f, ~0, QueryTriggerInteraction.Ignore), spawn.name + " has 0.5 m obstacle clearance");
            float runwayHalf = surface.localScale.z * 0.5f;
            foreach (Transform mark in surface.parent)
            {
                if (!mark.name.StartsWith("Threshold_") && !mark.name.StartsWith("CentreStripe_")) continue;
                check(Mathf.Abs(mark.localPosition.z) + mark.localScale.z * 0.5f < runwayHalf, mark.name + " fits within the runway ends");
            }
            report.Add("Runway: " + surface.localScale.z + " x " + surface.localScale.x + " m");
            report.Add("Hangar remains a solid massing placeholder, not an enterable building.");
            report.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/WorldBuilder");
            File.WriteAllLines("Docs/WorldBuilder/GreyboxAdditionsReview.txt", report);
            if (failures > 0) throw new InvalidOperationException("Additions review found " + failures + " failures. See GreyboxAdditionsReview.txt.");
        }
    }
}
