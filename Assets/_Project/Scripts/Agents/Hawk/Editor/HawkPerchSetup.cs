using RunwayPanic.Agents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RunwayPanic.AITools
{
    /// <summary>
    /// Tools > IS > Hawk > Place Sandbox Perches: adds 8 <see cref="BirdPoint"/> Perch markers to the open sandbox scene,
    /// each exactly on a surface of the Main greybox (tower roof, terminal roof edge, two hangar roof corners, three
    /// aircraft tail fins, shelter B roof), under a parent "HawkPerches". Running it again replaces that parent.
    /// Undoable; changes only the open scene. Refuses to run on Main.unity (the World Builder's scene) — the positions
    /// below are what to send to the World Builder for Main.
    /// </summary>
    public static class HawkPerchSetup
    {
        private const string ParentName = "HawkPerches";

        // Positions read from Main.unity's greybox (world-space top surfaces, 17 Oct). Spread over both sides of the
        // runway, from 3.3 m (tail fins) to 14.6 m (tower), so the height, distance and sun terms all matter.
        private static readonly (string name, Vector3 position)[] Perches =
        {
            ("Perch_ControlTower", new Vector3(-29.4f, 14.6f, 24.4f)),  // tower roof, corner facing the runway
            ("Perch_TerminalRoof", new Vector3(20.0f, 6.4f, 25.9f)),    // terminal roof, runway-side edge
            ("Perch_Hangar_NE", new Vector3(-31.9f, 6.0f, -0.6f)),      // hangar roof, north-east corner
            ("Perch_Hangar_SE", new Vector3(-31.9f, 6.0f, -24.8f)),     // hangar roof, south-east corner
            ("Perch_PlaneA_Tail", new Vector3(-17.6f, 3.5f, -28.45f)),  // utility aircraft tail fin
            ("Perch_PlaneB_Tail", new Vector3(36.45f, 3.3f, -1.0f)),    // light aircraft B tail fin
            ("Perch_PlaneD_Tail", new Vector3(41.15f, 3.3f, 23.0f)),    // light aircraft D tail fin
            ("Perch_ShelterB_Roof", new Vector3(21.0f, 4.3f, -12.0f)),  // shelter B roof centre
        };

        [MenuItem("Tools/IS/Hawk/Place Sandbox Perches")]
        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Hawk Perches", "Run this in Edit mode (stop Play first).", "OK");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path.EndsWith("/Main.unity"))
            {
                EditorUtility.DisplayDialog("Hawk Perches", "Main.unity belongs to the World Builder. Open your sandbox scene first.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Place Sandbox Perches");
            int undoGroup = Undo.GetCurrentGroup();

            GameObject old = GameObject.Find(ParentName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);

            var parent = new GameObject(ParentName);
            Undo.RegisterCreatedObjectUndo(parent, "Add " + ParentName);

            foreach ((string perchName, Vector3 position) in Perches)
            {
                var marker = new GameObject(perchName);
                Undo.RegisterCreatedObjectUndo(marker, "Add " + perchName);
                marker.transform.SetParent(parent.transform, false);
                marker.transform.position = position;
                Undo.AddComponent<BirdPoint>(marker); // defaults: type Perch, radius 1.5 m
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = parent;
            Debug.Log($"[HawkSetup] placed {Perches.Length} Perch markers under '{ParentName}' in {scene.name}. Save the scene (Ctrl+S).");
        }
    }
}
