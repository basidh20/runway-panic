using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.AI;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.EditorTools;
using RunwayPanic.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Gull.EditorTools
{
    /// <summary>Connects the existing Main Gull markers only; never installs a spawner or configures waves.</summary>
    public static class MainGullIntegration
    {
        public const string MainPath = "Assets/_Project/Scenes/Main.unity";
        public const string SpeciesPath = "Assets/_Project/ScriptableObjects/Birds/BirdSpecies_Gull.asset";
        private const string RootName = "GullRuntime_Main";

        [MenuItem("Tools/IS/Gull/Connect Gull Runtime in Main")]
        public static void Connect()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the current scene before connecting Main.");
            if (SceneManager.GetActiveScene().path != MainPath) EditorSceneManager.OpenScene(MainPath);
            var scene = SceneManager.GetActiveScene();
            var config = AssetDatabase.LoadAssetAtPath<GullStrategyConfig>(GullG1Setup.ConfigPath);
            var species = AssetDatabase.LoadAssetAtPath<BirdSpeciesData>(SpeciesPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GullG1Setup.PrefabPath);
            var fod = AssetDatabase.LoadAssetAtPath<GameObject>(GullFodSetup.PrefabPath)?.GetComponent<FodItem>();
            var points = InMain<BirdPoint>().Where(p => p.Type == BirdPointType.FodSpawn).ToArray();
            var managers = InMain<FodManager>(true); var kettles = InMain<GullKettle>(true);
            var brain = prefab != null ? prefab.GetComponent<GullBrain>() : null;
            if (config == null || species == null || fod == null || species.Prefab != prefab || brain == null || brain.Config != config ||
                species.Species != BirdSpecies.Gull || new SerializedObject(brain).FindProperty("previewStateCycle").boolValue ||
                points.Length != 8 || managers.Length > 1 || kettles.Length > 1 || InMain<FlightGrid>().Length != 1 ||
                InMain<GameManager>().Length != 1 || InMain<PlayerController>().Length != 1 || InMain<BirdTestSpawner>(true).Length != 0 || InMain<BirdHealth>(true).Length != 0)
                throw new InvalidOperationException("Expected Main foundations, eight FOD markers, real Gull prefab/config mapping and no saved test spawners/birds.");
            foreach (var point in points)
                if (point.TryGetComponent<FodSpawnPoint>(out var existing)) CheckExisting(existing, config, species, fod);
            if (managers.Length == 1) CheckExisting(managers[0], config, null, null);
            if (kettles.Length == 1) CheckExisting(kettles[0], config, null, null);
            if (InMain<FodSpawnPoint>(true).Any(p => !points.Any(marker => marker.gameObject == p.gameObject)))
                throw new InvalidOperationException("An unexpected FOD supply exists outside the eight intended markers.");
            var root = scene.GetRootGameObjects().SingleOrDefault(o => o.name == RootName);
            if (root != null && (root.transform.position != Vector3.zero || root.transform.rotation != Quaternion.identity || root.transform.localScale != Vector3.one ||
                root.GetComponents<Component>().Length != 1 || root.transform.Cast<Transform>().Any(t =>
                    t.childCount != 0 || t.GetComponents<Component>().Length != 2 ||
                    (t.name != "FodManager" || t.GetComponent<FodManager>() == null) && (t.name != "GullKettle" || t.GetComponent<GullKettle>() == null))))
                throw new InvalidOperationException("Unexpected edits to the Gull runtime root; review before changing it.");
            string markerSnapshot = MarkerSnapshot();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Connect Main Gull runtime");
            try
            {
                if (root == null)
                {
                    root = new GameObject(RootName); Undo.RegisterCreatedObjectUndo(root, "Add Gull runtime root");
                }
                foreach (var point in points)
                {
                    var supply = point.GetComponent<FodSpawnPoint>() ?? Undo.AddComponent<FodSpawnPoint>(point.gameObject);
                    var data = new SerializedObject(supply);
                    data.FindProperty("prefab").objectReferenceValue = fod;
                    data.FindProperty("config").objectReferenceValue = config;
                    data.FindProperty("species").objectReferenceValue = species;
                    data.ApplyModifiedProperties();
                }
                var manager = managers.Length == 1 ? managers[0] : Create<FodManager>(root.transform, "FodManager");
                var kettle = kettles.Length == 1 ? kettles[0] : Create<GullKettle>(root.transform, "GullKettle");
                foreach (var component in new MonoBehaviour[] { manager, kettle })
                {
                    var data = new SerializedObject(component); data.FindProperty("config").objectReferenceValue = config; data.ApplyModifiedProperties();
                }
                if (!new SerializedObject(kettle).FindProperty("dispatchEnabled").boolValue)
                    throw new InvalidOperationException("Existing kettle dispatch is disabled; review that edit explicitly.");
                Undo.FlushUndoRecordObjects();
                if (MarkerSnapshot() != markerSnapshot) throw new InvalidOperationException("A marker's layout/metadata changed unexpectedly.");
                Validate();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Main.");
                Debug.Log("[MainGull] Saved eight supplies, one manager and one configured kettle. No birds/spawners/waves added.");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        private static T Create<T>(Transform parent, string name) where T : Component
        {
            var item = new GameObject(name); Undo.RegisterCreatedObjectUndo(item, "Add " + name); item.transform.SetParent(parent, false);
            return Undo.AddComponent<T>(item);
        }
        private static void CheckExisting(MonoBehaviour component, GullStrategyConfig config, BirdSpeciesData species, FodItem prefab)
        {
            var data = new SerializedObject(component);
            foreach (var pair in new[] { ("config", (UnityEngine.Object)config), ("species", (UnityEngine.Object)species), ("prefab", (UnityEngine.Object)prefab) })
            {
                var property = data.FindProperty(pair.Item1);
                if (property != null && property.objectReferenceValue != null && property.objectReferenceValue != pair.Item2)
                    throw new InvalidOperationException("Refusing to replace an existing different " + pair.Item1 + " on " + component.name);
            }
        }
        private static string MarkerSnapshot() => string.Join("\n", InMain<BirdPoint>(true).OrderBy(p => p.name, StringComparer.Ordinal).Select(p =>
            p.name + "|" + p.Type + "|" + p.Group + "|" + p.Radius + "|" + p.transform.position + "|" + p.transform.rotation + "|" + p.transform.localScale));
        public static T[] InMain<T>(bool includeInactive = false) where T : Component => MainPlayerIntegration.InScene<T>(includeInactive);

        [MenuItem("Tools/IS/Gull/Validate Main Gull Runtime")]
        public static void Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPath)
                throw new InvalidOperationException("Open Main outside Play first.");
            var config = AssetDatabase.LoadAssetAtPath<GullStrategyConfig>(GullG1Setup.ConfigPath);
            var species = AssetDatabase.LoadAssetAtPath<BirdSpeciesData>(SpeciesPath);
            var fod = AssetDatabase.LoadAssetAtPath<GameObject>(GullFodSetup.PrefabPath)?.GetComponent<FodItem>();
            var lines = new List<string> { "Main Gull runtime — focused Edit-mode reference validation, Unity " + Application.unityVersion };
            int failures = 0;
            Action<bool, string> check = (ok, message) => { lines.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; };
            var supplies = InMain<FodSpawnPoint>(true);
            check(supplies.Length == 8 && supplies.All(p => p.isActiveAndEnabled && p.GetComponent<BirdPoint>().Type == BirdPointType.FodSpawn &&
                Matches(p, "prefab", fod) && Matches(p, "config", config) && Matches(p, "species", species)), "Eight enabled existing markers have Gull supply references");
            var managers = InMain<FodManager>(true); var kettles = InMain<GullKettle>(true);
            check(managers.Length == 1 && managers[0].isActiveAndEnabled && Matches(managers[0], "config", config), "One active configured FOD manager");
            check(kettles.Length == 1 && kettles[0].isActiveAndEnabled && Matches(kettles[0], "config", config) &&
                new SerializedObject(kettles[0]).FindProperty("dispatchEnabled").boolValue, "One active configured kettle with dispatch enabled");
            var points = InMain<BirdPoint>();
            check(points.Count(p => p.Type == BirdPointType.KettleCenter) == 1 && points.Count(p => p.Type == BirdPointType.FodSpawn) == 8 &&
                points.Count(p => p.Type == BirdPointType.Roost) == 3, "One kettle centre, eight FOD positions and three Pigeon roosts retained");
            check(points.Count(p => p.Type == BirdPointType.Spawn) == 8 && points.Count(p => p.Type == BirdPointType.Perch) == 8 &&
                points.Count(p => p.Type == BirdPointType.Stash) == 2 && points.Count(p => p.Type == BirdPointType.ShelterExit) == 2, "Shared spawn/Hawk/Crow marker counts retained");
            check(InMain<FlightGrid>().Length == 1 && InMain<GameManager>().Length == 1 && InMain<PlayerController>().Length == 1, "Single existing grid, GameManager and player retained");
            check(InMain<BirdTestSpawner>(true).Length == 0 && InMain<BirdHealth>(true).Length == 0, "No test spawner or live bird saved into Main");
            check(config != null && species != null && species.Prefab != null && species.Prefab.GetComponent<GullBrain>() != null && species.Prefab == AssetDatabase.LoadAssetAtPath<GameObject>(GullG1Setup.PrefabPath) &&
                species.Prefab.GetComponent<GullBrain>()?.Config == config && !new SerializedObject(species.Prefab.GetComponent<GullBrain>()).FindProperty("previewStateCycle").boolValue,
                "Shared Gull species resolves to the actual strategy prefab, not the G1 label preview");
            lines.Add("Environment readiness only. S2's production WaveManager controls automatic waves separately; this check does not validate that wave loop.");
            lines.Add("GameManager.TotalWaves=" + InMain<GameManager>().Single().TotalWaves + "; the current team decision is four waves.");
            lines.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/IS/Gull"); File.WriteAllLines("Docs/IS/Gull/MainEditValidation.txt", lines);
            if (failures != 0) throw new InvalidOperationException("Main Gull reference check failed; see MainEditValidation.txt.");
        }
        private static bool Matches(MonoBehaviour component, string field, UnityEngine.Object value) =>
            value != null && new SerializedObject(component).FindProperty(field).objectReferenceValue == value;
    }
}
