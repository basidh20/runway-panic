using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Focused inspection and small spacing corrections for manual airport dressing.</summary>
    public static class AirportDressingReview
    {
        [MenuItem("Tools/Runway Panic/Review Airport Additions")]
        private static void Review()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || SceneManager.GetActiveScene().path != "Assets/_Project/Scenes/Main.unity") return;
            Physics.SyncTransforms();
            var report = new StringBuilder();
            var root = GameObject.Find("Environment/Airport_FlowerBeds");
            var ground = GameObject.Find("Environment/Ground_100x80");
            var flowers = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (IsFlower(t)) flowers.Add(t);
            int blocked = 0, crowded = 0, ungrounded = 0;
            foreach (var t in flowers)
            {
                var renderer = t.GetComponentInChildren<Renderer>();
                if (!Physics.Raycast(t.position + Vector3.up * 60f, Vector3.down, out RaycastHit hit,
                    65f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.gameObject != ground)
                {
                    blocked++;
                    report.AppendLine($"Flower on obstacle: {t.name} at {t.position:F3}; surface: {(hit.collider != null ? hit.collider.name : "none")}");
                }
                if (renderer != null && (renderer.bounds.min.y < -0.03f || renderer.bounds.min.y > 0.06f))
                {
                    ungrounded++;
                    report.AppendLine($"Flower base height: {t.name} at {t.position:F3}, minY={renderer.bounds.min.y:F3}");
                }
                for (int i = 0; i < flowers.IndexOf(t); i++)
                {
                    var delta = flowers[i].position - t.position;
                    if (new Vector2(delta.x, delta.z).sqrMagnitude < 0.05f) crowded++;
                }
            }
            report.Insert(0, $"Flowers: {flowers.Count}; on obstacles: {blocked}; incorrect base height: {ungrounded}; close pairs (<0.224m): {crowded}\n");

            var hangar = GameObject.Find("Environment/Buildings/Hangar_Greybox").transform;
            var plane = GameObject.Find("P_Prop_Aircraft_WhiteRed");
            if (plane != null)
            {
                var local = new Bounds();
                bool first = true;
                foreach (var filter in plane.GetComponentsInChildren<MeshFilter>())
                    foreach (var vertex in filter.sharedMesh.vertices)
                    {
                        var p = hangar.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                        if (first) { local = new Bounds(p, Vector3.zero); first = false; }
                        else local.Encapsulate(p);
                    }
                report.AppendLine($"Hangar plane: {plane.transform.position:F3}, yaw={plane.transform.eulerAngles.y:F2}, scale={plane.transform.localScale:F3}");
                report.AppendLine($"Plane exact hangar-local bounds: min {local.min:F3}, max {local.max:F3}, size {local.size:F3}; interior x=-6.6..6.6, z=-12.1..12.1, floorY=0.05");
                foreach (var a in plane.GetComponentsInChildren<Collider>())
                    foreach (var b in hangar.GetComponentsInChildren<Collider>())
                        if (b.name != "Floor" && Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                            b, b.transform.position, b.transform.rotation, out _, out float depth) && depth > 0.01f)
                            report.AppendLine($"Plane overlap: {a.name} with {b.name}, depth {depth:F3}m");
                TerminalFlowerDressing.CaptureView(hangar.TransformPoint(new Vector3(11f, 2.2f, 0f)),
                    hangar.TransformPoint(new Vector3(-1f, 1.0f, 0f)), "/private/tmp/RunwayPanic-HangarReview.png");
            }
            TerminalFlowerDressing.CaptureView(new Vector3(-24f, 5f, 16f), new Vector3(-32f, 0.4f, 29f),
                "/private/tmp/RunwayPanic-TowerFlowersReview.png");
            File.WriteAllText("/private/tmp/RunwayPanic-DressingReview.txt", report.ToString());
            Debug.Log("[Dressing Review] Wrote /private/tmp/RunwayPanic-DressingReview.txt and two preview images.");
        }

        private static bool IsFlower(Transform t)
        {
            return t.name.StartsWith("SM_Daisy_") || t.name.StartsWith("SM_Dandelion_")
                || t.name.StartsWith("SM_Hyacinth_") || t.name.StartsWith("SM_Rose_");
        }

        [MenuItem("Tools/Runway Panic/Tidy Close Tower Flowers")]
        private static void TidyTowerFlowers()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || scene.path != "Assets/_Project/Scenes/Main.unity") return;
            var root = GameObject.Find("Environment/Airport_FlowerBeds");
            var tower = root.transform.Find("ControlTower_Bed");
            var ground = GameObject.Find("Environment/Ground_100x80");
            var flowers = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (IsFlower(t)) flowers.Add(t);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Separate overlapping tower flowers");
            Physics.SyncTransforms();
            int adjusted = 0;
            foreach (var t in flowers)
            {
                if (!t.IsChildOf(tower) || HasSpace(t, t.position, flowers, 0.224f)) continue;
                Vector3 original = t.position;
                bool found = false;
                for (int ring = 1; ring <= 3 && !found; ring++)
                    for (int direction = 0; direction < 12; direction++)
                    {
                        float angle = direction * Mathf.PI / 6f;
                        Vector3 point = original + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * 0.12f);
                        if (!HasSpace(t, point, flowers, 0.26f)
                            || !Physics.Raycast(point + Vector3.up * 60f, Vector3.down, out RaycastHit hit,
                                65f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.gameObject != ground) continue;
                        Undo.RecordObject(t, "Separate overlapping flower");
                        t.position = point;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                        adjusted++;
                        found = true;
                        break;
                    }
            }
            Undo.CollapseUndoOperations(group);
            if (adjusted > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Review();
            Debug.Log($"[Dressing Review] Separated {adjusted} close tower flowers; kept the other flowers and hangar plane in place.");
        }

        private static bool HasSpace(Transform flower, Vector3 point, List<Transform> flowers, float distance)
        {
            foreach (var other in flowers)
            {
                if (other == flower) continue;
                Vector3 delta = other.position - point;
                if (delta.x * delta.x + delta.z * delta.z < distance * distance) return false;
            }
            return true;
        }
    }
}
