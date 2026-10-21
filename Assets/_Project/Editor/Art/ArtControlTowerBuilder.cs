using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Control tower (SM_Prop_ControlTower, 20 Oct) - replaces the World Builder's four-cube tower placeholder.
    ///
    /// Tools > S3 > Build Control Tower Prefab
    ///   P_Prop_ControlTower: the model (9 parts, 1 atlas material + 2 emissive) and a "Colliders" child with one convex
    ///   MeshCollider per COL_ mesh of SM_Prop_ControlTower_Collision.fbx (shaft, platform, cab, roof, 2 utility
    ///   blocks - closed convex prisms that came with the tower). Octagonal tiers cannot be matched by boxes: a box
    ///   around the 8.1 m platform pokes 1.7 m out at every corner. Re-running rebuilds the colliders only.
    ///
    /// Tools > S3 > Swap Control Tower Into Main
    ///   In the open Main scene, replaces the cubes under ControlTower_Placeholder with the prefab. The group keeps
    ///   its name: MainAirportDressing (grass) and PathfindingSelfTest find the tower by it. The tower is turned so
    ///   its front (the side without the utility building) faces the runway; the utility building ends up on the
    ///   north-west side, 2 m from the north fence. Perch_ControlTower (hawk vantage) is moved onto the new roof:
    ///   its old point is in the air beside the octagonal roof. The scene is only marked dirty: check it, then save.
    ///   Undo works.
    /// </summary>
    public static class ArtControlTowerBuilder
    {
        const string ModelPath = ArtBudgets.ModelsRoot + "Props/SM_Prop_ControlTower.fbx";
        const string CollisionPath = ArtBudgets.ModelsRoot + "Props/SM_Prop_ControlTower_Collision.fbx";
        const string PrefabPath = "Assets/_Project/Prefabs/Environment/P_Prop_ControlTower.prefab";
        const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        const string SupportPath = "Environment/Background/ControlTower_Placeholder";
        const string PerchPath = "HawkPerches/Perch_ControlTower";
        const string CollidersName = "Colliders";

        // The placeholder's centre (AirportGreyboxBuilder: cubes at x -33, z 28) on the ground.
        static readonly Vector3 TowerPosition = new Vector3(-33f, 0f, 28f);
        // Model front = +Z; +90 deg turns it to +X, towards the runway (x = 0).
        static readonly Quaternion TowerRotation = Quaternion.Euler(0f, 90f, 0f);
        // On the roof collider (top 15.085 m = 21.55 m x 0.7), 3 m from the centre towards the runway (south-east),
        // the same side as the old corner point (-29.4, 14.6, 24.4). Keep in sync with MainHawkPerchIntegration.
        static readonly Vector3 PerchPosition = new Vector3(-30.9f, 15.09f, 25.9f);

        [MenuItem("Tools/S3/Build Control Tower Prefab")]
        public static void BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var colliderMeshes = AssetDatabase.LoadAllAssetsAtPath(CollisionPath).OfType<Mesh>()
                .Where(m => m.name.StartsWith("COL_")).OrderBy(m => m.name).ToArray();
            if (model == null || colliderMeshes.Length == 0)
            {
                Debug.LogWarning($"[Art] {ModelPath} or the COL_ meshes in {CollisionPath} not found - export " +
                                 "SM_Prop_ControlTower.blend with export_fbx.py --all first.");
                return;
            }

            bool exists = File.Exists(PrefabPath);
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("P_Prop_ControlTower");
            try
            {
                // Root = the tower object; child = the model, so a re-export from Blender updates the placed tower.
                if (!exists)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                }
                BuildColliders(root, colliderMeshes);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (success) Debug.Log($"[Art] {(exists ? "Updated" : "Created")} {PrefabPath}: " + Describe(root));
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        // Static background prop: no Rigidbody, layer Default, not Static - the same as the placeholder cubes.
        // The COL_ meshes were exported with Apply Transform from objects at the tower origin, so their vertices are
        // already in prefab space: each collider sits at the identity under "Colliders".
        static void BuildColliders(GameObject root, Mesh[] meshes)
        {
            var old = root.transform.Find(CollidersName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var parent = new GameObject(CollidersName).transform;
            parent.SetParent(root.transform, false);
            foreach (var mesh in meshes)
            {
                var collider = new GameObject(mesh.name).AddComponent<MeshCollider>();
                collider.transform.SetParent(parent, false);
                collider.sharedMesh = mesh;
                collider.convex = true;
            }
        }

        // Renderer vs collider bounds: the two FBX files must line up (same origin, same axis conversion).
        static string Describe(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var colliders = root.GetComponentsInChildren<MeshCollider>();
            Bounds visual = renderers[0].bounds, solid = colliders[0].sharedMesh.bounds;
            foreach (var r in renderers) visual.Encapsulate(r.bounds);
            foreach (var c in colliders) solid.Encapsulate(c.sharedMesh.bounds);
            int tris = ArtBudgets.CountTriangles(root);
            return $"{renderers.Length} renderers, {tris} tris, {colliders.Length} convex colliders; " +
                   $"visual bounds min {visual.min:F2} max {visual.max:F2}, collider bounds min {solid.min:F2} max {solid.max:F2}";
        }

        [MenuItem("Tools/S3/Swap Control Tower Into Main")]
        public static void SwapIntoMain()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != MainScenePath)
            {
                Debug.LogWarning($"[Art] Open {MainScenePath} (outside Play mode) first.");
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var support = GameObject.Find(SupportPath);
            var perch = GameObject.Find(PerchPath);
            if (prefab == null || support == null || perch == null)
            {
                Debug.LogWarning($"[Art] Missing {(prefab == null ? PrefabPath + " (run Build Control Tower Prefab)" : support == null ? SupportPath : PerchPath)} - no changes made.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Swap Control Tower Into Main");

            bool done = support.transform.childCount == 1 &&
                        PrefabUtility.GetCorrespondingObjectFromSource(support.transform.GetChild(0).gameObject) == prefab;
            if (done)
            {
                Debug.Log("[Art] ControlTower_Placeholder already uses P_Prop_ControlTower - tower skipped.");
            }
            else
            {
                // No old renderers or colliders may stay under the new model (same rule as the aircraft swap).
                for (int i = support.transform.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(support.transform.GetChild(i).gameObject);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, support.transform);
                instance.transform.SetPositionAndRotation(TowerPosition, TowerRotation);
                instance.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(instance, "Swap Control Tower Into Main");
                Debug.Log("[Art] ControlTower_Placeholder: 4 cubes -> P_Prop_ControlTower");
            }

            // The perch must stand on the new roof collider (MainHawkPerchIntegration's surface check: hit within
            // 0.1 m below, normal up, collider under ControlTower_Placeholder).
            Physics.SyncTransforms();
            if (!Physics.Raycast(PerchPosition + Vector3.up * 0.15f, Vector3.down, out var hit, 0.3f, ~0, QueryTriggerInteraction.Ignore) ||
                !hit.collider.transform.IsChildOf(support.transform) || hit.normal.y < 0.9f)
            {
                Undo.RevertAllDownToGroup(group);
                Debug.LogWarning($"[Art] No tower roof under {PerchPosition:F2} - swap undone. Check the tower position/rotation.");
                return;
            }
            Undo.RecordObject(perch.transform, "Move Perch_ControlTower onto the new roof");
            perch.transform.position = PerchPosition;
            Debug.Log($"[Art] Perch_ControlTower -> {PerchPosition:F2} (roof collider {hit.collider.name} at y {hit.point.y:F3}).");

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Art] Swap Control Tower Into Main done. Check it, save the scene, then run " +
                      "Tools > Runway Panic > Validate Main Hawk Perches and Tools > IS > Pathfinding Self-Test.");
        }
    }
}
