using System;
using System.Linq;
using RunwayPanic.Agents.Crow;
using RunwayPanic.Interaction;
using RunwayPanic.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Wires the existing Main doors and Crow zones without editing shared prefabs.</summary>
    public static class MainSafeRoomIntegration
    {
        [MenuItem("Tools/Runway Panic/Integrate Main Timed Shelters")]
        public static void Integrate()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode before integrating shelters.");
            if (GameObject.Find("TimedShelters") != null || MainPlayerIntegration.InScene<SafeRoomController>(true).Length != 0)
                throw new InvalidOperationException("Timed shelter objects already exist; review before adding another set.");

            var zones = MainPlayerIntegration.InScene<ShelterZone>(true);
            var groups = new[] { "ShelterA", "ShelterB" };
            var selectedZones = groups.Select(group => zones.Single(zone => zone.Group == group)).ToArray();
            var doors = new[] { "Shelter_A", "Shelter_B" }.Select(name =>
            {
                var shelter = GameObject.Find("Environment/Buildings/" + name);
                if (shelter == null) throw new InvalidOperationException("Missing " + name);
                return shelter.GetComponentsInChildren<Door>(true).Single();
            }).ToArray();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                var root = new GameObject("TimedShelters");
                Undo.RegisterCreatedObjectUndo(root, "Integrate timed shelters");
                var rooms = new SafeRoomController[2];
                for (int i = 0; i < rooms.Length; i++)
                {
                    var room = new GameObject(groups[i] + "_Controller");
                    room.SetActive(false);
                    room.transform.SetParent(root.transform, false);
                    room.transform.position = selectedZones[i].Centre;
                    rooms[i] = room.AddComponent<SafeRoomController>();
                    var config = new SerializedObject(rooms[i]);
                    config.FindProperty("zone").objectReferenceValue = selectedZones[i];
                    config.FindProperty("door").objectReferenceValue = doors[i];
                    config.FindProperty("useSeconds").floatValue = 15f;
                    config.FindProperty("cooldownSeconds").floatValue = 30f;
                    config.ApplyModifiedPropertiesWithoutUndo();
                    room.SetActive(true);
                }
                var hud = root.AddComponent<SafeRoomStatusHUD>();
                var hudConfig = new SerializedObject(hud);
                var array = hudConfig.FindProperty("rooms");
                array.arraySize = rooms.Length;
                for (int i = 0; i < rooms.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = rooms[i];
                hudConfig.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main.");
                Debug.Log("[TimedShelters] Two rooms wired to existing Crow zones and shared doors (15s / 30s).");
            }
            catch { Undo.RevertAllDownToGroup(undoGroup); throw; }
            finally { Undo.CollapseUndoOperations(undoGroup); }
        }
    }
}
