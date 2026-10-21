using System.IO;
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

        private static void CapturePreview(Vector3 centre)
        {
            var go = new GameObject("FlowerPreview_TEMP") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 55f;
            camera.farClipPlane = 200f;
            camera.transform.position = centre + new Vector3(2f, 1.8f, -4f);
            camera.transform.LookAt(centre + Vector3.up * 0.3f);
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
                File.WriteAllBytes("/private/tmp/RunwayPanic-Flowers.png", image.EncodeToPNG());
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
