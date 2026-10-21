using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Places a few decorative flower beds on Main's terminal lawns.</summary>
    public static class TerminalFlowerDressing
    {
        private const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string Prefabs = "Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/";
        private const string RootName = "Airport_FlowerBeds";

        [MenuItem("Tools/Runway Panic/Add Airport Flower Beds")]
        private static void AddBeds()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainScenePath)
            {
                Debug.LogWarning("[Flowers] Open Main outside Play mode first.");
                return;
            }
            if (GameObject.Find(RootName) != null)
            {
                Debug.LogWarning("[Flowers] Airport flower beds already exist; no duplicates added.");
                return;
            }

            var environment = GameObject.Find("Environment");
            var ground = GameObject.Find("Environment/Ground_100x80");
            var daisy = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "SM_Daisy_Single.prefab");
            var yellow = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "SM_Dandelion_Small.prefab");
            var blue = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "SM_Hyacinth_PastellBlue_Big.prefab");
            if (environment == null || ground == null || daisy == null || yellow == null || blue == null)
            {
                Debug.LogError("[Flowers] Required airport objects or flower prefabs are missing.");
                return;
            }

            // Explicit lawn positions: two beds in front of the terminal (its wall is at
            // z=26), and one beside the control tower. The runway is within x=-7..7.
            Vector3[] centres =
            {
                new Vector3(14f, 0f, 23.8f),
                new Vector3(26f, 0f, 23.8f),
                new Vector3(-39.5f, 0f, 22.5f)
            };
            Vector2[] offsets =
            {
                new Vector2(-1.10f, -0.25f), new Vector2(-0.55f, 0.35f),
                new Vector2(0.05f, -0.32f), new Vector2(0.60f, 0.32f),
                new Vector2(1.10f, -0.12f), new Vector2(-0.80f, -0.65f),
                new Vector2(0.85f, -0.60f), new Vector2(-0.25f, 0.60f),
                new Vector2(0.30f, 0.65f)
            };

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add airport flower beds");
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add airport flower beds");
            root.transform.SetParent(environment.transform, false);
            Physics.SyncTransforms();
            int placed = 0;
            for (int bed = 0; bed < centres.Length; bed++)
            {
                var holder = new GameObject(bed < 2 ? "Terminal_Bed_" + (bed + 1) : "ControlTower_Bed");
                Undo.RegisterCreatedObjectUndo(holder, "Add flower bed");
                holder.transform.SetParent(root.transform, false);
                holder.transform.position = centres[bed];
                for (int i = 0; i < offsets.Length; i++)
                {
                    Vector3 point = centres[bed] + new Vector3(offsets[i].x, 0f, offsets[i].y);
                    // Reject the runway and any paved surface, building or prop above ground.
                    if (Mathf.Abs(point.x) < 9f || !Physics.Raycast(point + Vector3.up * 60f,
                        Vector3.down, out RaycastHit hit, 65f, ~0, QueryTriggerInteraction.Ignore)
                        || hit.collider.gameObject != ground)
                        continue;

                    GameObject prefab = i < 5 ? daisy : (i < 7 ? yellow : blue);
                    var flower = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
                    Undo.RegisterCreatedObjectUndo(flower, "Add flower");
                    flower.transform.SetPositionAndRotation(hit.point,
                        Quaternion.Euler(0f, (i * 137f + bed * 41f) % 360f, 0f));
                    var renderer = flower.GetComponentInChildren<Renderer>();
                    if (renderer != null && renderer.bounds.size.y > 0.001f)
                    {
                        float height = (i < 5 ? 0.52f : (i < 7 ? 0.46f : 0.60f)) + (i % 3) * 0.025f;
                        flower.transform.localScale *= height / renderer.bounds.size.y;
                        // Place the base on the soil while blooms rise above 0.36 m grass.
                        flower.transform.position += Vector3.up * (hit.point.y + 0.005f - renderer.bounds.min.y);
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(flower.transform);
                    placed++;
                }
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(centres[0] + Vector3.up * 0.3f,
                    Quaternion.Euler(22f, 0f, 0f), 6f, false);
            CapturePreview(centres[0]);
            Debug.Log($"[Flowers] Saved {placed} flowers in three small lawn beds. Preview: /private/tmp/RunwayPanic-Flowers.png");
        }

        private sealed class Patch
        {
            public readonly string name;
            public readonly Vector3 centre;
            public readonly int count;
            public readonly Vector2 spread;
            public readonly int[] palette;

            public Patch(string name, float x, float z, int count, float width, float depth, params int[] palette)
            {
                this.name = name;
                centre = new Vector3(x, 0f, z);
                this.count = count;
                spread = new Vector2(width, depth);
                this.palette = palette;
            }
        }

        [MenuItem("Tools/Runway Panic/Add Varied Lawn Flowers")]
        private static void AddVariedFlowers()
        {
            var scene = SceneManager.GetActiveScene();
            var root = GameObject.Find("Environment/" + RootName);
            var ground = GameObject.Find("Environment/Ground_100x80");
            var grass = GameObject.Find("Environment/C2_AirportDressing/Grass_Patches");
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainScenePath
                || root == null || ground == null || grass == null)
            {
                Debug.LogWarning("[Flowers] Open Main outside Play mode with the original flower beds and grass.");
                return;
            }
            string[] names = { "SM_Daisy_Single", "SM_Dandelion_Small", "SM_Hyacinth_PastellBlue_Big", "SM_Rose_Red_Open" };
            var prefabs = new GameObject[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + names[i] + ".prefab");
                if (prefabs[i] == null)
                {
                    Debug.LogError("[Flowers] Missing flower prefab: " + names[i]);
                    return;
                }
            }

            // Each patch has its own shape, number of flowers, colour mix and random seed.
            // Red roses are confined to the landscaped terminal side.
            Patch[] patches =
            {
                new Patch("Terminal_Side_Roses", 33f, 25f, 4, 1.0f, 0.65f, 3, 2, 3, 0),
                new Patch("Tower_Lawn_Daisies", -26f, 25f, 5, 1.8f, 0.6f, 0),
                new Patch("Hangar_Side_Yellow", -39f, -25.5f, 3, 0.8f, 1.2f, 1),
                new Patch("Hangar_Lawn_Mix", -41f, 4f, 6, 1.5f, 0.8f, 0, 1, 0),
                new Patch("West_Lawn_Scatter", -16f, -28f, 4, 2.2f, 1.1f, 1, 0),
                new Patch("Shelter_B_Lawn_Blue", 13f, -23f, 7, 1.5f, 1.0f, 2, 0, 2),
                new Patch("East_Lawn_Blue_Yellow", 33f, 3f, 5, 0.9f, 1.8f, 2, 1),
                new Patch("South_Lawn_Daisies", 25f, -29f, 3, 1.7f, 0.7f, 0)
            };

            // Read the existing grass-card centres, so flowers cannot land on bare paving
            // even when that decorative paving has no collider.
            var turf = new HashSet<Vector2Int>();
            foreach (var filter in grass.GetComponentsInChildren<MeshFilter>())
            {
                var vertices = filter.sharedMesh.vertices;
                for (int i = 0; i + 1 < vertices.Length; i += 8)
                {
                    Vector3 p = filter.transform.TransformPoint((vertices[i] + vertices[i + 1]) * 0.5f);
                    turf.Add(TurfCell(p));
                }
            }
            var occupied = new List<Vector3>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                occupied.Add(renderer.transform.position);

            Physics.SyncTransforms();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add varied lawn flowers");
            var existingGroup = root.transform.Find("Varied_Lawn_Flowers");
            var group = existingGroup != null ? existingGroup.gameObject : new GameObject("Varied_Lawn_Flowers");
            if (existingGroup == null)
            {
                Undo.RegisterCreatedObjectUndo(group, "Add varied lawn flowers");
                group.transform.SetParent(root.transform, false);
            }
            int placed = 0;
            for (int p = 0; p < patches.Length; p++)
            {
                Patch patch = patches[p];
                var existingPatch = group.transform.Find(patch.name);
                // Re-running can complete an empty patch without duplicating existing flowers.
                if (existingPatch != null && existingPatch.childCount > 0) continue;
                var holder = existingPatch != null ? existingPatch.gameObject : new GameObject(patch.name);
                if (existingPatch == null)
                {
                    Undo.RegisterCreatedObjectUndo(holder, "Add varied flower patch");
                    holder.transform.SetParent(group.transform, false);
                }
                else Undo.RecordObject(holder.transform, "Move empty flower patch to grass");
                holder.transform.position = patch.centre;
                var random = new System.Random(7219 + p * 137);
                for (int i = 0; i < patch.count; i++)
                {
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                        float radius = Mathf.Sqrt((float)random.NextDouble());
                        Vector3 point = patch.centre + new Vector3(Mathf.Cos(angle) * radius * patch.spread.x,
                            0f, Mathf.Sin(angle) * radius * patch.spread.y);
                        if (Mathf.Abs(point.x) < 10f || !turf.Contains(TurfCell(point))
                            || occupied.Exists(other => (new Vector2(other.x - point.x, other.z - point.z)).sqrMagnitude < 0.22f)
                            || !Physics.Raycast(point + Vector3.up * 60f, Vector3.down, out RaycastHit hit,
                                65f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.gameObject != ground)
                            continue;

                        int species = patch.palette[i % patch.palette.Length];
                        var flower = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[species], holder.transform);
                        Undo.RegisterCreatedObjectUndo(flower, "Add varied flower");
                        flower.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                        var renderer = flower.GetComponentInChildren<Renderer>();
                        float height = (species == 2 ? 0.55f : 0.47f) + (float)random.NextDouble() * 0.12f;
                        flower.transform.localScale *= height / Mathf.Max(renderer.bounds.size.y, 0.001f);
                        flower.transform.position += Vector3.up * (hit.point.y + 0.005f - renderer.bounds.min.y);
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(flower.transform);
                        occupied.Add(point);
                        placed++;
                        break;
                    }
                }
            }
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = group;
            SceneView.lastActiveSceneView?.LookAt(patches[0].centre + Vector3.up * 0.3f,
                Quaternion.Euler(22f, 0f, 0f), 6f, false);
            CapturePreview(patches[0].centre, "/private/tmp/RunwayPanic-Flowers-Varied.png");
            CapturePreview(patches[5].centre, "/private/tmp/RunwayPanic-Flowers-Blue.png");
            Debug.Log($"[Flowers] Saved {placed} additional flowers in eight different lawn patches.");
        }

        private static Vector2Int TurfCell(Vector3 point)
        {
            return new Vector2Int(Mathf.RoundToInt(point.x / 0.5f), Mathf.RoundToInt(point.z / 0.5f));
        }

        private static void CapturePreview(Vector3 centre, string path = "/private/tmp/RunwayPanic-Flowers.png")
        {
            CaptureView(centre + new Vector3(2f, 1.8f, -4f), centre + Vector3.up * 0.3f, path);
        }

        internal static void CaptureView(Vector3 position, Vector3 targetPoint, string path)
        {
            var go = new GameObject("FlowerPreview_TEMP") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 55f;
            camera.farClipPlane = 200f;
            camera.transform.position = position;
            camera.transform.LookAt(targetPoint);
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var target = RenderTexture.GetTemporary(960, 540, 24);
            var old = RenderTexture.active;
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(go);
            }
        }
    }
}
