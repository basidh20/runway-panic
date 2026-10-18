using RunwayPanic.Agents;
using RunwayPanic.Agents.Hawk;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.AITools
{
    /// <summary>
    /// Tools > IS > Hawk > Add Hawk Test Tools: adds to the open sandbox scene
    /// <list type="bullet">
    /// <item>a <see cref="HawkDebugOverlay"/> (F4 toggles the panel), and</item>
    /// <item>a second <see cref="BirdTestSpawner"/> that spawns 5 pigeons on <b>N</b> — enough other birds (3+) for the
    /// hawk's Herd mode (H6). The pigeons have no brain yet, so they hover where they spawn.</item>
    /// </list>
    /// Running it again replaces both. Undoable; changes only the open scene. Refuses to run on Main.unity.
    /// </summary>
    public static class HawkTestToolsSetup
    {
        private const string OverlayName = "HawkDebugOverlay";
        private const string SpawnerName = "HerdTestSpawner";
        private const string FlockPrefabPath = "Assets/_Project/Prefabs/Birds/P_Bird_Pigeon.prefab";
        private const int FlockCount = 5;
        private const Key FlockKey = Key.N;
        // Above the middle of the runway, clear of the greybox buildings (see HawkPerchSetup for their positions).
        private static readonly Vector3 FlockPosition = new Vector3(0f, 8f, 10f);

        [MenuItem("Tools/IS/Hawk/Add Hawk Test Tools")]
        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Hawk Test Tools", "Run this in Edit mode (stop Play first).", "OK");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path.EndsWith("/Main.unity"))
            {
                EditorUtility.DisplayDialog("Hawk Test Tools", "Main.unity belongs to the World Builder. Open your sandbox scene first.", "OK");
                return;
            }

            var flockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlockPrefabPath);
            if (flockPrefab == null)
            {
                EditorUtility.DisplayDialog("Hawk Test Tools", $"Prefab not found: {FlockPrefabPath}", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Add Hawk Test Tools");
            int undoGroup = Undo.GetCurrentGroup();

            Replace(OverlayName);
            var overlay = new GameObject(OverlayName);
            Undo.RegisterCreatedObjectUndo(overlay, "Add " + OverlayName);
            Undo.AddComponent<HawkDebugOverlay>(overlay);

            Replace(SpawnerName);
            var spawnerObject = new GameObject(SpawnerName);
            Undo.RegisterCreatedObjectUndo(spawnerObject, "Add " + SpawnerName);
            spawnerObject.transform.position = FlockPosition;
            var spawner = Undo.AddComponent<BirdTestSpawner>(spawnerObject);

            // The spawner's settings are private serialized fields: set them through the serialized object.
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("prefab").objectReferenceValue = flockPrefab;
            serialized.FindProperty("count").intValue = FlockCount;
            serialized.FindProperty("spawnKey").intValue = (int)FlockKey;
            serialized.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = spawnerObject;
            Debug.Log($"[HawkSetup] added {OverlayName} (F4) and {SpawnerName} ({FlockCount} × {flockPrefab.name} on {FlockKey}) to {scene.name}. Save the scene (Ctrl+S).");
        }

        private static void Replace(string objectName)
        {
            GameObject old = GameObject.Find(objectName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
        }
    }
}
