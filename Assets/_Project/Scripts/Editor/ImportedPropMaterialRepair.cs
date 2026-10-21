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
    /// <summary>Repairs explicitly selected imported prefab materials and scenery collision.</summary>
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
        private static readonly string[] FireTrucks =
        {
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Vehicles/ForestFireTruck1.prefab",
            "Assets/Scenes/Jucho/FirefightersPack/Models/Prefabs/Vehicles/ForestFireTruck3.prefab"
        };

        [MenuItem("Tools/Runway Panic/Fix Five Imported Prop Materials")]
        private static void Repair()
        {
            RepairPrefabs(Targets, "SelectedProps", Manifest);
        }

        [MenuItem("Tools/Runway Panic/Fix Fire Truck Materials")]
        private static void RepairFireTrucks()
        {
            RepairPrefabs(FireTrucks, "FireTrucks", "Docs/WorldBuilder/FireTruckMaterials.paths");
        }

        private static void RepairPrefabs(string[] targets, string label, string manifest)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before repairing imported materials.");
            foreach (string path in targets)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new FileNotFoundException("Required imported prefab is missing.", path);

            var upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
            if (upgraders.Count == 0) throw new InvalidOperationException("Unity's URP material converter is unavailable.");
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "ImportedProps");
            var copies = new Dictionary<Material, Material>();
            var report = new StringBuilder();
            foreach (string path in targets)
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
            string preview = "/private/tmp/RunwayPanic-" + label + ".png";
            CapturePreviews(targets, preview);
            WriteCommitManifest(targets, manifest);
            File.WriteAllText("/private/tmp/RunwayPanic-" + label + "-Report.txt", report.ToString());
            Selection.objects = targets.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();
            Debug.Log($"[Imported Props] Repaired and verified {targets.Length} {label} prefabs. Preview: {preview}");
        }

        private static void CapturePreviews(string[] targets, string path)
        {
            const int width = 480, height = 320;
            int rows = Mathf.CeilToInt(targets.Length / 3f);
            var sheet = new Texture2D(width * 3, height * rows, TextureFormat.RGB24, false);
            var background = new Color[width * 3 * height * rows];
            for (int i = 0; i < background.Length; i++) background[i] = new Color(0.18f, 0.20f, 0.23f);
            sheet.SetPixels(background);
            try
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var preview = new PreviewRenderUtility();
                    Texture2D result = null;
                    try
                    {
                        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(targets[i]));
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
                        sheet.SetPixels((i % 3) * width, (rows - 1 - i / 3) * height, width, height, result.GetPixels());
                    }
                    finally
                    {
                        if (result != null) Object.DestroyImmediate(result);
                        preview.Cleanup();
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(path, sheet.EncodeToPNG());
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

        [MenuItem("Tools/Runway Panic/Review Fire Truck Placement")]
        private static void ReviewTruckPlacement()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || SceneManager.GetActiveScene().path != "Assets/_Project/Scenes/Main.unity") return;
            var report = new StringBuilder();
            var player = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.gameObject.scene == SceneManager.GetActiveScene());
            report.AppendLine("Player height: " + (player != null ? player.height * player.transform.lossyScale.y : 1.8f));
            int index = 0;
            foreach (var truck in PlacedTrucks())
            {
                Bounds local = LocalVisualBounds(truck), world = VisualBounds(truck);
                report.AppendLine($"{truck.name}: position={truck.transform.position:F3}, scale={truck.transform.localScale:F3}, yaw={truck.transform.eulerAngles.y:F2}, local size={local.size:F3}, world size={world.size:F3}, min={world.min:F3}, max={world.max:F3}, colliders={truck.GetComponentsInChildren<Collider>().Count(c => c.enabled && !c.isTrigger)}");
                var direction = new Vector3(1f, 0.65f, index == 0 ? 1f : -1f).normalized;
                TerminalFlowerDressing.CaptureView(world.center + direction * Mathf.Max(5f, world.extents.magnitude * 2.6f),
                    world.center, "/private/tmp/RunwayPanic-PlacedTruck-" + index + ".png");
                index++;
            }
            File.WriteAllText("/private/tmp/RunwayPanic-TruckPlacement.txt", report.ToString());
            Debug.Log("[Imported Props] Truck review: /private/tmp/RunwayPanic-TruckPlacement.txt");
        }

        private static GameObject[] PlacedTrucks()
        {
            var scene = SceneManager.GetActiveScene();
            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(t => t.gameObject.scene == scene && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
                    && FireTrucks.Contains(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)))
                .Select(t => t.gameObject).Distinct().OrderBy(t => t.name).ToArray();
        }

        [MenuItem("Tools/Runway Panic/Fix Parked Fire Truck Scale and Collision")]
        private static void FixParkedTrucks()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/_Project/Scenes/Main.unity")
                throw new InvalidOperationException("Open Main outside Play mode first.");
            var trucks = PlacedTrucks();
            if (trucks.Length == 0) throw new InvalidOperationException("No fire-truck instances found in Main.");
            foreach (string path in trucks.Select(t => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t)).Distinct())
            {
                string backup = "/private/tmp/RunwayPanic-TruckCollision-Backup/" + path;
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!File.Exists(backup)) File.Copy(path, backup);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // The main body contains cab, chassis and wheels. Roof equipment and
                    // mirrors do not expand this box into a large invisible obstruction.
                    var filters = root.GetComponentsInChildren<MeshFilter>();
                    var body = filters.FirstOrDefault(f => f.name == root.name)
                        ?? filters.OrderByDescending(f => f.GetComponent<Renderer>().bounds.size.sqrMagnitude).First();
                    if (body.GetComponent<Collider>() == null)
                    {
                        var collider = body.gameObject.AddComponent<BoxCollider>();
                        collider.center = body.sharedMesh.bounds.center;
                        collider.size = body.sharedMesh.bounds.size;
                        collider.isTrigger = false;
                    }
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new IOException("Could not save truck collider: " + path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                AssetDatabase.ImportAsset(path);
            }

            var player = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.gameObject.scene == scene);
            float playerHeight = player != null ? player.height * player.transform.lossyScale.y : 1.8f;
            var offsets = new List<Vector3>();
            for (int x = -8; x <= 8; x++) for (int z = -12; z <= 12; z++)
                offsets.Add(new Vector3(x * 0.5f, 0f, z * 0.5f));
            offsets = offsets.OrderBy(v => v.sqrMagnitude).ThenBy(v => Mathf.Abs(v.x)).ToList();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Scale and park fire trucks");
            Physics.SyncTransforms();
            var report = new StringBuilder();
            var obstacles = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.enabled && !c.isTrigger && c.gameObject.scene == scene && c.name != "Ground_100x80"
                    && c.GetComponentInParent<CharacterController>() == null).ToArray();
            foreach (var truck in trucks)
            {
                Vector3 original = truck.transform.position;
                Undo.RecordObject(truck.transform, "Scale and position parked truck");
                truck.transform.localScale *= (playerHeight * (3.1f / 1.8f)) / VisualBounds(truck).size.y;
                var own = truck.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
                bool found = false;
                foreach (var offset in offsets)
                {
                    truck.transform.position = original + offset;
                    Bounds bounds = VisualBounds(truck);
                    float floor = 0f;
                    foreach (var surface in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    {
                        if (surface.name != "Apron_A" && surface.name != "Apron_B" && surface.name != "Entrance_Apron") continue;
                        Bounds area = surface.bounds;
                        if (bounds.center.x >= area.min.x && bounds.center.x <= area.max.x
                            && bounds.center.z >= area.min.z && bounds.center.z <= area.max.z)
                            floor = Mathf.Max(floor, area.max.y);
                    }
                    truck.transform.position += Vector3.up * (floor + 0.003f - bounds.min.y);
                    bounds = VisualBounds(truck);
                    if (bounds.min.x < -49f || bounds.max.x > 49f || bounds.min.z < -39f || bounds.max.z > 39f
                        || (bounds.min.x < 7.9f && bounds.max.x > -7.9f)
                        || CrossesXZ(bounds, -15.8f, -7f, 10.5f, 13.5f)
                        || CrossesXZ(bounds, 7f, 15.8f, -13.5f, -10.5f)) continue;
                    bool overlap = false;
                    foreach (var a in own)
                        foreach (var b in obstacles)
                        {
                            if (b.transform.IsChildOf(truck.transform)) continue;
                            if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                                b, b.transform.position, b.transform.rotation, out _, out float depth) && depth > 0.005f)
                                overlap = true;
                        }
                    if (overlap) continue;
                    found = true;
                    break;
                }
                if (!found)
                {
                    Undo.RevertAllDownToGroup(group);
                    throw new InvalidOperationException("No nearby clear parking spot found for " + truck.name);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(truck.transform);
                report.AppendLine($"{truck.name}: moved {Vector3.Distance(new Vector3(original.x, 0, original.z), new Vector3(truck.transform.position.x, 0, truck.transform.position.z)):F2}m horizontally; no solid overlaps; runway and shelter entrance clear.");
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            ReviewTruckPlacement();
            foreach (var truck in trucks)
            {
                var collider = truck.GetComponentInChildren<Collider>();
                Bounds bounds = collider.bounds;
                float reach = bounds.size.magnitude + 2f;
                int blocked = 0;
                foreach (var direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    Vector3 centre = bounds.center;
                    centre.y = VisualBounds(truck).min.y + playerHeight * 0.5f;
                    var hits = Physics.CapsuleCastAll(centre - direction * reach - Vector3.up * 0.5f,
                        centre - direction * reach + Vector3.up * 0.5f, 0.3f, direction, reach * 2f,
                        ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Any(h => h.collider.transform.IsChildOf(truck.transform))) blocked++;
                }
                report.AppendLine($"{truck.name}: player-sized capsule sweeps blocked from {blocked}/4 directions.");
            }
            File.AppendAllText("/private/tmp/RunwayPanic-TruckPlacement.txt", report.ToString());
            WriteCommitManifest(FireTrucks, "Docs/WorldBuilder/FireTruckMaterials.paths");
            Debug.Log("[Imported Props] Parked trucks saved. " + report);
        }

        private static bool CrossesXZ(Bounds bounds, float minX, float maxX, float minZ, float maxZ)
        {
            return bounds.min.x < maxX && bounds.max.x > minX && bounds.min.z < maxZ && bounds.max.z > minZ;
        }

        private static Bounds LocalVisualBounds(GameObject root)
        {
            var result = new Bounds();
            bool first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Bounds mesh = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        private static void WriteCommitManifest(string[] targets = null, string manifest = Manifest)
        {
            var paths = new SortedSet<string>();
            var assets = new List<string>(AssetDatabase.GetDependencies(targets ?? Targets, true));
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
            paths.Add(manifest);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest));
            File.WriteAllLines(manifest, paths);
        }
    }
}
