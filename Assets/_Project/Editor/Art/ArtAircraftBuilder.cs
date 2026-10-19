using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Parked aircraft (SM_Prop_Aircraft, 19 Oct) - replaces the World Builder's light-aircraft greyboxes.
    ///
    /// Tools > S3 > Build Aircraft Prefabs
    ///   P_Prop_Aircraft          base livery from Blender: grey body, teal tail  (side planes)
    ///   P_Prop_Aircraft_WhiteRed Prefab Variant: only the atlas material changes (middle plane). The colours come
    ///                            from SourceArt/Tools/aircraft_liveries.json -> T_Prop_Aircraft_WhiteRed_Atlas_Albedo.
    ///   Collision is a compound of primitives fitted to the measured parts (Docs/WorldBuilder/AircraftArtHandoff.md:
    ///   "prefer simple compound collision ... do not use one large box around the whole wingspan"). Re-running the
    ///   menu rebuilds the colliders and the variant's material; other components on the prefabs are kept.
    ///
    /// Tools > S3 > Swap Aircraft Into Main (Plane B, C, D)
    ///   In the open Main scene, replaces the greybox child of Plane_B/C/D with the prefabs above. The roots, their
    ///   names and their parking transforms stay (the World Builder's validator looks them up by name). Plane_A (the
    ///   utility greybox) is not touched. The scene is only marked dirty: check it, then save. Undo works.
    /// </summary>
    public static class ArtAircraftBuilder
    {
        const string ModelPath = ArtBudgets.ModelsRoot + "Props/SM_Prop_Aircraft.fbx";
        const string PrefabFolder = "Assets/_Project/Prefabs/Environment/Aircraft/";
        const string BasePrefabPath = PrefabFolder + "P_Prop_Aircraft.prefab";
        const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        const string CollidersName = "Colliders";

        readonly struct Livery
        {
            public readonly string Prefab;
            public readonly string Material; // atlas material in Art/Materials/Props

            public Livery(string prefab, string material)
            {
                Prefab = prefab;
                Material = material;
            }
        }

        static readonly Livery WhiteRed = new Livery("P_Prop_Aircraft_WhiteRed", "M_Prop_Aircraft_WhiteRed_Atlas");

        // Which plane gets which livery: the middle of the eastern row is white/red, the two beside it grey/teal.
        static readonly (string Root, string Prefab)[] Swaps =
        {
            ("Plane_B_Greybox", "P_Prop_Aircraft"),
            ("Plane_C_Greybox", "P_Prop_Aircraft_WhiteRed"),
            ("Plane_D_Greybox", "P_Prop_Aircraft"),
        };

        // Prefab-local metres (+Z = nose, Y up, origin on the ground under the fuselage centre), measured from
        // SourceArt/Blender/Props/SM_Prop_Aircraft.blend on 19 Oct (8.01 m span, 7.73 m long, 3.08 m tall).
        // Measured: fuselage z -3.61..3.60, y 0.34..1.61 (widest 1.33 m at z 0); wings x +-4.0, y 1.31..1.54,
        // z -0.54..0.59 (swept ~9 deg); engines x +-1.17..1.72, y 0.72..1.43, z -0.27..1.33; fin z -1.51..-4.07,
        // y 1.26..3.08; tailplane x +-1.55 at y ~3.0; main gear x +-0.79, y 0..0.52; nose gear z 2.51..2.75.
        // The cockpit sits inside the fuselage capsule and the fans inside the engine boxes, so they get none.
        static readonly (string Name, Vector3 Center, Vector3 Size)[] Boxes =
        {
            // One thin slab for both wings: it blocks the 1.8 m player like the greybox wing (handoff rule 4)
            // but leaves the space under it open; the swept tips over-cover by at most ~0.4 m of chord.
            ("Wings", new Vector3(0f, 1.43f, 0.03f), new Vector3(8.0f, 0.23f, 1.13f)),
            ("Engine_L", new Vector3(1.44f, 1.07f, 0.53f), new Vector3(0.55f, 0.72f, 1.6f)),
            ("Engine_R", new Vector3(-1.44f, 1.07f, 0.53f), new Vector3(0.55f, 0.72f, 1.6f)),
            // Rear part of the swept fin only; its lower front is inside the fuselage capsule.
            ("Tail_Fin", new Vector3(0f, 2.24f, -3.19f), new Vector3(0.17f, 1.68f, 1.77f)),
            ("Tailplane", new Vector3(0f, 3.0f, -3.89f), new Vector3(3.1f, 0.1f, 0.5f)),
            ("Main_Gear", new Vector3(0f, 0.26f, -0.35f), new Vector3(1.58f, 0.52f, 0.36f)),
            ("Nose_Gear", new Vector3(0f, 0.225f, 2.63f), new Vector3(0.17f, 0.45f, 0.25f)),
        };

        static readonly Vector3 FuselageCenter = new Vector3(0f, 0.98f, 0f);
        const float FuselageRadius = 0.64f;
        const float FuselageLength = 7.21f;

        [MenuItem("Tools/Runway Panic/Replace Plane A With Model")]
        public static void ReplacePlaneA()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainScenePath || scene.isDirty)
            {
                Debug.LogWarning("[Art] Open saved Main outside Play mode before replacing Plane A.");
                return;
            }

            var root = GameObject.Find("Plane_A_Greybox");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
            var previous = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Environment/Greybox/P_Greybox_UtilityAircraft.prefab");
            var roost = GameObject.Find("Roost_PlaneA_Wing");
            var perch = GameObject.Find("Perch_PlaneA_Tail");
            if (root == null || prefab == null || previous == null || roost == null || perch == null ||
                root.transform.childCount != 1 || PrefabUtility.GetCorrespondingObjectFromSource(root.transform.GetChild(0).gameObject) != previous)
            {
                Debug.LogWarning("[Art] Plane A, its expected greybox, model or bird points were not found; no changes made.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Replace Plane A");
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one * 1.12f; // 8.97 m span, close to the 9 m greybox.
            Physics.SyncTransforms();
            var wing = instance.transform.Find("Colliders/Wings")?.GetComponent<Collider>();
            var tail = instance.transform.Find("Colliders/Tailplane")?.GetComponent<Collider>();
            Vector3 wingStart = root.transform.TransformPoint(new Vector3(3.1f, 6f, 0.03f));
            Vector3 tailStart = root.transform.TransformPoint(new Vector3(0f, 6f, -4.36f));
            if (wing == null || tail == null || !wing.Raycast(new Ray(wingStart, Vector3.down), out var wingHit, 10f) ||
                !tail.Raycast(new Ray(tailStart, Vector3.down), out var tailHit, 10f))
            {
                Undo.DestroyObjectImmediate(instance);
                Debug.LogWarning("[Art] Could not align Plane A bird points; kept the greybox unchanged.");
                return;
            }
            Undo.RecordObject(roost.transform, "Align Plane A bird points");
            Undo.RecordObject(perch.transform, "Align Plane A bird points");
            roost.transform.position = wingHit.point + Vector3.up * .02f;
            perch.transform.position = tailHit.point + Vector3.up * .02f;
            Undo.DestroyObjectImmediate(root.transform.GetChild(0).gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Art] Plane A now uses the scaled blue/teal aircraft; check and save Main.");
        }

        [MenuItem("Tools/S3/Build Aircraft Prefabs")]
        public static void BuildPrefabs()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogWarning($"[Art] {ModelPath} not found - export SM_Prop_Aircraft from Blender first.");
                return;
            }
            string materialPath = $"{ArtBudgets.MaterialsRoot}Props/{WhiteRed.Material}.mat";
            var whiteRed = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (whiteRed == null)
            {
                Debug.LogWarning($"[Art] {materialPath} not found - check SM_Prop_Aircraft.materials.json was imported " +
                                 "(Tools > S3 > Rebuild Materials From Manifests).");
                return;
            }

            if (!AssetDatabase.IsValidFolder(PrefabFolder.TrimEnd('/')))
                AssetDatabase.CreateFolder("Assets/_Project/Prefabs/Environment", "Aircraft");

            var basePrefab = BuildBase(model);
            if (basePrefab == null) return;
            BuildVariant(basePrefab, whiteRed);
            AssetDatabase.SaveAssets();
        }

        static GameObject BuildBase(GameObject model)
        {
            bool exists = File.Exists(BasePrefabPath);
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(BasePrefabPath) : new GameObject("P_Prop_Aircraft");
            try
            {
                // Root = the parked-aircraft object (the World Builder's validator and teammates add things here).
                // Child = the model, so a re-export from Blender updates every placed aircraft.
                if (!exists)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                }
                BuildColliders(root);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, BasePrefabPath, out bool success);
                if (success) Debug.Log($"[Art] {(exists ? "Updated" : "Created")} {BasePrefabPath}");
                return success ? saved : null;
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        // Static parked props: no Rigidbody, layer Default, not Static - the same as the greybox they replace.
        static void BuildColliders(GameObject root)
        {
            var old = root.transform.Find(CollidersName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var parent = new GameObject(CollidersName).transform;
            parent.SetParent(root.transform, false);

            var fuselage = new GameObject("Fuselage").AddComponent<CapsuleCollider>();
            fuselage.transform.SetParent(parent, false);
            fuselage.direction = 2; // Z axis, nose to tail
            fuselage.center = FuselageCenter;
            fuselage.radius = FuselageRadius;
            fuselage.height = FuselageLength;

            foreach (var (name, center, size) in Boxes)
            {
                var box = new GameObject(name).AddComponent<BoxCollider>();
                box.transform.SetParent(parent, false);
                box.center = center;
                box.size = size;
            }
        }

        static void BuildVariant(GameObject basePrefab, Material material)
        {
            string path = $"{PrefabFolder}{WhiteRed.Prefab}.prefab";
            bool exists = File.Exists(path);

            // New: an instance of the base saved as a new asset becomes a Prefab Variant (colliders are inherited).
            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(path)
                : (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                root.name = WhiteRed.Prefab;
                // One atlas slot per part (10 MeshRenderers): the variant only swaps the texture set, same mesh + UVs.
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterials = new[] { material };

                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (success) Debug.Log($"[Art] {(exists ? "Updated" : "Created")} {path}");
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        [MenuItem("Tools/S3/Swap Aircraft Into Main (Plane B, C, D)")]
        public static void SwapIntoMain()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != MainScenePath)
            {
                Debug.LogWarning($"[Art] Open {MainScenePath} (outside Play mode) first.");
                return;
            }

            int swapped = 0;
            foreach (var (rootName, prefabName) in Swaps)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}{prefabName}.prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[Art] {prefabName} not found - run Tools > S3 > Build Aircraft Prefabs first.");
                    return;
                }
                var root = GameObject.Find(rootName);
                if (root == null)
                {
                    Debug.LogWarning($"[Art] {rootName} not found in Main - skipped.");
                    continue;
                }
                if (root.transform.childCount == 1 &&
                    PrefabUtility.GetCorrespondingObjectFromSource(root.transform.GetChild(0).gameObject) == prefab)
                {
                    Debug.Log($"[Art] {rootName} already uses {prefabName} - skipped.");
                    continue;
                }

                // The handoff's rule 2: no old renderers or colliders may stay under the new model.
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(instance, "Swap Aircraft Into Main");
                swapped++;
                Debug.Log($"[Art] {rootName}: greybox -> {prefabName}");
            }

            if (swapped > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Art] Swap Aircraft Into Main: {swapped} plane(s) swapped. Check them, save the scene, then run " +
                      "Tools > Runway Panic > Validate Aircraft Greyboxes.");
        }
    }
}
