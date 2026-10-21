using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RunwayPanic.EditorTools
{
    /// <summary>Converts only the materials assigned to five selected imported prefabs.</summary>
    public static class ImportedPropMaterialRepair
    {
        private const string MaterialsFolder = "Assets/_Project/Art/Materials/ImportedProps";
        private const string Manifest = "Docs/WorldBuilder/SelectedImportedProps.paths";
        private static readonly string[] Targets =
        {
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Equipment/TrafficCone.prefab",
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Enviroment/Bush1.prefab",
            "Assets/Free_Street_Props/PBR_Prefabs/firehydrant_01_a_02.prefab",
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Equipment/ExtensionCord.prefab",
            "Assets/OlegWER/AbandonedAircraft/Prefabs/SM_aircraft_.prefab"
        };

        [MenuItem("Tools/Runway Panic/Fix Five Imported Prop Materials")]
        private static void Repair()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before repairing imported materials.");
            foreach (string path in Targets)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new FileNotFoundException("Required imported prefab is missing.", path);

            var upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
            if (upgraders.Count == 0) throw new InvalidOperationException("Unity's URP material converter is unavailable.");
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "ImportedProps");
            var copies = new Dictionary<Material, Material>();
            var report = new StringBuilder();
            foreach (string path in Targets)
            {
                // Imported prefabs are still untracked, so retain their original files too.
                string backup = "/private/tmp/RunwayPanic-SelectedProps-Backup/" + path;
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!File.Exists(backup)) File.Copy(path, backup);
                var root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials = renderer.sharedMaterials;
                        bool rendererChanged = false;
                        for (int i = 0; i < materials.Length; i++)
                        {
                            Material original = materials[i];
                            if (original == null) throw new InvalidOperationException("Missing material on " + path);
                            if (original.shader != null && original.shader.name.StartsWith("Universal Render Pipeline/")) continue;
                            if (!copies.TryGetValue(original, out Material converted))
                            {
                                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
                                string safeName = original.name.Replace(" ", "_").Replace("/", "_");
                                string copyPath = MaterialsFolder + "/M_" + safeName + "_" + guid.Substring(0, 8) + "_URP.mat";
                                converted = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
                                if (converted == null)
                                {
                                    converted = new Material(original) { name = Path.GetFileNameWithoutExtension(copyPath) };
                                    MaterialUpgrader.Upgrade(converted, upgraders, MaterialUpgrader.UpgradeFlags.None);
                                    if (converted.shader == null || !converted.shader.name.StartsWith("Universal Render Pipeline/"))
                                    {
                                        Object.DestroyImmediate(converted);
                                        throw new InvalidOperationException("Could not convert material " + original.name);
                                    }
                                    converted.enableInstancing = true;
                                    AssetDatabase.CreateAsset(converted, copyPath);
                                }
                                if (original.HasProperty("_MainTex") && converted.GetTexture("_BaseMap") != original.GetTexture("_MainTex"))
                                    throw new InvalidOperationException("Base texture did not transfer for " + original.name);
                                foreach (string texture in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" })
                                    if (original.HasProperty(texture) && original.GetTexture(texture) != converted.GetTexture(texture))
                                        throw new InvalidOperationException("Texture did not transfer: " + texture + " on " + original.name);
                                copies.Add(original, converted);
                            }
                            materials[i] = converted;
                            rendererChanged = true;
                        }
                        if (rendererChanged)
                        {
                            renderer.sharedMaterials = materials;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                            changed = true;
                        }
                    }
                    if (changed && PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new IOException("Could not save converted prefab " + path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                AssetDatabase.ImportAsset(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || !material.shader.isSupported
                            || !material.shader.name.StartsWith("Universal Render Pipeline/"))
                            throw new InvalidOperationException("A material is still incompatible on " + path);
                        report.AppendLine(Path.GetFileNameWithoutExtension(path) + " / " + renderer.name
                            + ": " + material.shader.name + ", colour texture=" + AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")));
                    }
            }
            AssetDatabase.SaveAssets();
            CapturePreviews();
            WriteCommitManifest();
            File.WriteAllText("/private/tmp/RunwayPanic-SelectedProps-Report.txt", report.ToString());
            Selection.objects = Targets.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();
            Debug.Log("[Imported Props] Repaired and verified all five prefabs. Previews: /private/tmp/RunwayPanic-SelectedProps.png");
        }

        private static void CapturePreviews()
        {
            const int width = 480, height = 320;
            var sheet = new Texture2D(width * 3, height * 2, TextureFormat.RGB24, false);
            var background = new Color[width * 3 * height * 2];
            for (int i = 0; i < background.Length; i++) background[i] = new Color(0.18f, 0.20f, 0.23f);
            sheet.SetPixels(background);
            try
            {
                for (int i = 0; i < Targets.Length; i++)
                {
                    var preview = new PreviewRenderUtility();
                    Texture2D result = null;
                    try
                    {
                        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Targets[i]));
                        preview.AddSingleGO(model);
                        var renderers = model.GetComponentsInChildren<Renderer>();
                        Bounds bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                        float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
                        float distance = radius / Mathf.Sin(15f * Mathf.Deg2Rad) * 1.1f;
                        preview.camera.fieldOfView = 30f;
                        preview.camera.nearClipPlane = 0.01f;
                        preview.camera.farClipPlane = distance + radius * 4f + 10f;
                        preview.camera.backgroundColor = new Color(0.18f, 0.20f, 0.23f);
                        preview.camera.clearFlags = CameraClearFlags.SolidColor;
                        preview.camera.transform.position = bounds.center + new Vector3(1f, 0.6f, -1f).normalized * distance;
                        preview.camera.transform.LookAt(bounds.center);
                        preview.lights[0].intensity = 1.3f;
                        preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
                        preview.lights[1].intensity = 0.8f;
                        preview.ambientColor = new Color(0.5f, 0.5f, 0.5f);
                        preview.BeginStaticPreview(new Rect(0, 0, width, height));
                        preview.Render(true);
                        result = preview.EndStaticPreview();
                        sheet.SetPixels((i % 3) * width, (1 - i / 3) * height, width, height, result.GetPixels());
                    }
                    finally
                    {
                        if (result != null) Object.DestroyImmediate(result);
                        preview.Cleanup();
                    }
                }
                sheet.Apply();
                File.WriteAllBytes("/private/tmp/RunwayPanic-SelectedProps.png", sheet.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(sheet); }
        }

        [MenuItem("Tools/Runway Panic/Review Placed Imported Props")]
        private static void ReviewPlacedProps()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || SceneManager.GetActiveScene().path != "Assets/_Project/Scenes/Main.unity") return;
            Physics.SyncTransforms();
            var report = new StringBuilder();
            foreach (var prop in PlacedProps())
            {
                Bounds bounds = VisualBounds(prop);
                var colliders = prop.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
                var support = Physics.RaycastAll(new Vector3(bounds.center.x, 60f, bounds.center.z),
                    Vector3.down, 65f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => !h.transform.IsChildOf(prop.transform)).OrderBy(h => h.distance).FirstOrDefault();
                report.AppendLine($"{prop.name}: position={prop.transform.position:F3}, min={bounds.min:F3}, max={bounds.max:F3}, solid colliders={colliders.Length}, support={(support.collider != null ? support.collider.name : "none")}@{support.point.y:F3}");
                if (prop.name.StartsWith("SM_aircraft_"))
                {
                    foreach (var filter in prop.GetComponentsInChildren<MeshFilter>())
                        report.AppendLine($"  Aircraft mesh {filter.name}: {filter.sharedMesh.vertexCount} vertices, readable={filter.sharedMesh.isReadable}");
                    float distance = Mathf.Max(4f, bounds.extents.magnitude * 2.3f);
                    TerminalFlowerDressing.CaptureView(bounds.center + new Vector3(-1f, 0.65f, -1f).normalized * distance,
                        bounds.center, "/private/tmp/RunwayPanic-PlacedPlane.png");
                }
            }
            TerminalFlowerDressing.CaptureView(new Vector3(-25.5f, 2.4f, -26f), new Vector3(-31f, 0.8f, -22.5f),
                "/private/tmp/RunwayPanic-PlacedEquipment.png");
            File.WriteAllText("/private/tmp/RunwayPanic-PlacedProps.txt", report.ToString());
            Debug.Log("[Imported Props] Reviewed current placements: /private/tmp/RunwayPanic-PlacedProps.txt");
        }

        private static GameObject[] PlacedProps()
        {
            var scene = SceneManager.GetActiveScene();
            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(t => t.gameObject.scene == scene && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
                    && Targets.Contains(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)))
                .Select(t => t.gameObject).Distinct().ToArray();
        }

        private static Bounds VisualBounds(GameObject prop)
        {
            var renderers = prop.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        [MenuItem("Tools/Runway Panic/Fix Placed Imported Prop Collision")]
        private static void FixPlacedCollision()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/_Project/Scenes/Main.unity")
                throw new InvalidOperationException("Open Main outside Play mode first.");
            // These objects are stationary scenery. A non-convex collider follows the wreck's
            // actual surfaces, rather than filling all the empty space around its wings.
            foreach (int index in new[] { 0, 3, 4 })
            {
                string path = Targets[index];
                string backup = "/private/tmp/RunwayPanic-PropCollision-Backup/" + path;
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!File.Exists(backup)) File.Copy(path, backup);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                        if (index == 3)
                        {
                            var box = filter.gameObject.AddComponent<BoxCollider>();
                            box.center = filter.sharedMesh.bounds.center;
                            box.size = filter.sharedMesh.bounds.size;
                            box.isTrigger = false;
                        }
                        else
                        {
                            var collider = filter.gameObject.AddComponent<MeshCollider>();
                            collider.sharedMesh = filter.sharedMesh;
                            collider.convex = index == 0;
                            collider.isTrigger = false;
                        }
                    }
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new IOException("Could not save collision on " + path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                AssetDatabase.ImportAsset(path);
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Correct imported prop placement");
            Physics.SyncTransforms();
            int grounded = 0, mounted = 0;
            var placed = PlacedProps();
            foreach (var prop in placed)
            {
                Bounds bounds = VisualBounds(prop);
                if (prop.name.StartsWith("TrafficCone") || prop.name.StartsWith("SM_aircraft_"))
                {
                    float floor = 0f;
                    foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    {
                        if (renderer.name != "Apron_A" && renderer.name != "Apron_B" && renderer.name != "Entrance_Apron") continue;
                        Bounds surface = renderer.bounds;
                        if (bounds.center.x >= surface.min.x && bounds.center.x <= surface.max.x
                            && bounds.center.z >= surface.min.z && bounds.center.z <= surface.max.z)
                            floor = Mathf.Max(floor, surface.max.y);
                    }
                    float correction = floor + 0.003f - bounds.min.y;
                    if (Mathf.Abs(correction) > 0.005f)
                    {
                        Undo.RecordObject(prop.transform, "Rest prop on its surface");
                        prop.transform.position += Vector3.up * correction;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);
                        grounded++;
                    }
                }
                else if (prop.name.StartsWith("ExtensionCord"))
                {
                    Vector3 back = -prop.transform.right;
                    var wall = Physics.RaycastAll(bounds.center, back, 0.75f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(h => !h.transform.IsChildOf(prop.transform)).OrderBy(h => h.distance).FirstOrDefault();
                    if (wall.collider == null) continue;
                    float extent = Vector3.Dot(new Vector3(Mathf.Abs(back.x), Mathf.Abs(back.y), Mathf.Abs(back.z)), bounds.extents);
                    float gap = wall.distance - extent;
                    if (gap > 0.02f && gap < 0.2f)
                    {
                        Undo.RecordObject(prop.transform, "Mount reel against wall");
                        prop.transform.position += back * (gap + 0.003f);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);
                        mounted++;
                    }
                }
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            ReviewPlacedProps();
            var evidence = new StringBuilder();
            foreach (var plane in placed.Where(p => p.name.StartsWith("SM_aircraft_")))
            {
                Bounds bounds = VisualBounds(plane);
                float reach = Mathf.Max(bounds.size.x, bounds.size.z) + 2f;
                int blocked = 0;
                foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    Vector3 centre = bounds.center;
                    var hits = Physics.CapsuleCastAll(centre - direction * reach - Vector3.up * 0.45f,
                        centre - direction * reach + Vector3.up * 0.45f, 0.3f, direction, reach * 2f,
                        ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Any(h => h.collider.transform.IsChildOf(plane.transform))) blocked++;
                }
                evidence.AppendLine($"{plane.name}: character-sized capsule sweeps blocked by aircraft from {blocked}/4 directions; solid mesh colliders={plane.GetComponentsInChildren<MeshCollider>().Count(c => c.enabled && !c.isTrigger && c.sharedMesh != null)}");
            }
            File.AppendAllText("/private/tmp/RunwayPanic-PlacedProps.txt", evidence.ToString());
            WriteCommitManifest();
            Debug.Log($"[Imported Props] Saved solid plane/cone/reel collision; corrected {grounded} ground heights and {mounted} wall mount. " + evidence);
        }

        private static void WriteCommitManifest()
        {
            var paths = new SortedSet<string>();
            var assets = new List<string>(AssetDatabase.GetDependencies(Targets, true));
            assets.Add("Assets/_Project/Scripts/Editor/ImportedPropMaterialRepair.cs");
            foreach (string asset in assets)
            {
                if (!asset.StartsWith("Assets/")) continue;
                paths.Add(asset);
                if (File.Exists(asset + ".meta")) paths.Add(asset + ".meta");
                string parent = Path.GetDirectoryName(asset)?.Replace('\\', '/');
                while (parent != null && parent != "Assets")
                {
                    if (File.Exists(parent + ".meta")) paths.Add(parent + ".meta");
                    parent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
                }
            }
            paths.Add(Manifest);
            Directory.CreateDirectory(Path.GetDirectoryName(Manifest));
            File.WriteAllLines(Manifest, paths);
        }
    }
}
