using System;
using System.Linq;
using RunwayPanic.Interaction;
using RunwayPanic.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>One-time, guarded replacement of selected Main cover with S2's connected prefab variant.</summary>
    public static class MainCoverIntegration
    {
        private const string MainPath = "Assets/_Project/Scenes/Main.unity";
        private const string FixedPath = "Assets/_Project/Prefabs/Environment/Greybox/P_Greybox_Barricade.prefab";
        private const string MovablePath = "Assets/_Project/Prefabs/Interactables/P_Barricade_Movable.prefab";
        private static readonly string[] Targets = { "Cover_02", "Cover_03", "Cover_05" };

        [MenuItem("Tools/Runway Panic/Integrate Main Movable Cover")]
        public static void Integrate()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var cover = GameObject.Find("Cover")?.transform;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MovablePath);
            if (cover == null || cover.childCount != 6 || prefab == null || prefab.GetComponent<Barricade>() == null ||
                scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerPusher>(true)).Count() != 1)
                throw new InvalidOperationException("Expected six cover pieces, S2's movable prefab and one player pusher.");

            var originals = Targets.Select(n => cover.Find(n)).ToArray();
            if (originals.Any(t => t == null || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) != FixedPath ||
                                   t.localScale != Vector3.one || t.GetComponent<Barricade>() != null))
                throw new InvalidOperationException("Expected unmodified, unit-scale fixed prefab instances. No cover was changed.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Integrate Main movable cover");
            try
            {
                foreach (var original in originals)
                {
                    var replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    Undo.RegisterCreatedObjectUndo(replacement, "Place movable cover");
                    replacement.transform.SetParent(cover, false);
                    replacement.transform.localPosition = original.localPosition;
                    replacement.transform.localRotation = original.localRotation;
                    replacement.transform.SetSiblingIndex(original.GetSiblingIndex());
                    replacement.name = original.name + "_Movable";
                    PrefabUtility.RecordPrefabInstancePropertyModifications(replacement.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(replacement);
                    Undo.DestroyObjectImmediate(original.gameObject);
                }
                if (cover.childCount != 6 || cover.GetComponentsInChildren<Barricade>().Length != 3)
                    throw new InvalidOperationException("Unexpected cover count; reverting the replacements.");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main.");
                Undo.CollapseUndoOperations(group);
                Debug.Log("[MainCover] Saved three movable and three fixed cover pieces in Main.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }
    }
}
