using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Core;
using RunwayPanic.Interaction;
using RunwayPanic.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Connects the supplied spawner to Main; opt-in check uses actual pickup triggers and delay.</summary>
    [InitializeOnLoad]
    public static class MainPickupIntegration
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interactables/P_PickupSpawner.prefab";
        private const string ReportPath = "Docs/WorldBuilder/PickupValidation.txt";
        private const string SessionKey = "RunwayPanic.MainPickupSmoke";
        private static readonly List<string> Report = new List<string>();
        private static PickupSpawner spawner;
        private static Transform[] markers;
        private static PlayerController player;
        private static CharacterController controller;
        private static Pickup target;
        private static Vector3 targetPosition;
        private static int phase, failures, medkitsBefore, shieldsBefore;
        private static bool running, collected, earlyChecked, shieldBefore;
        private static float collectedAt;
        private static double started, phaseStarted;

        static MainPickupIntegration() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Tools/Runway Panic/Integrate Main Pickups")]
        public static void Integrate()
        {
            RequireMain();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var points = FindMarkers();
            var existing = MainPlayerIntegration.InScene<PickupSpawner>(true);
            if (prefab == null || points.Length != 4 || existing.Length > 1 ||
                (existing.Length == 1 && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(existing[0].gameObject) != PrefabPath))
                throw new InvalidOperationException("Expected four original pickup markers and at most one shared spawner. No changes made.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Integrate Main pickup spawning");
            try
            {
                PickupSpawner instance;
                if (existing.Length == 0)
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
                    Undo.RegisterCreatedObjectUndo(root, "Install shared pickup spawner");
                    instance = root.GetComponent<PickupSpawner>();
                }
                else instance = existing[0];
                var data = new SerializedObject(instance);
                var bindings = data.FindProperty("spawnPoints");
                bindings.arraySize = points.Length;
                for (int i = 0; i < points.Length; i++) bindings.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
                data.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                Undo.FlushUndoRecordObjects();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save Main.");
                Debug.Log("[MainPickups] Connected one shared spawner to Pickup_00–03. Shared prefab defaults retained.");
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        [MenuItem("Tools/Runway Panic/Run Main Pickup Smoke Check")]
        public static void RunSmokeCheck()
        {
            RequireMain();
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save Main first.");
            SessionState.SetBool(SessionKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                phase = failures = 0;
                running = true; earlyChecked = false;
                started = phaseStarted = EditorApplication.timeSinceStartup;
                Report.Clear();
                Report.Add("Main pickup integration — one focused Play-mode smoke check");
                Report.Add("Unity: " + Application.unityVersion);
                Report.Add("Actual CharacterController trigger collection; real 25-second replacement delay, no timer overrides.");
                Report.Add("Random pickup sample only, not exhaustive medkit/shield/inventory testing. Test wave event is Play-mode-only.");
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && running)
            { Check(false, "Check interrupted"); Finish(false); }
            else if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(SessionKey, false);
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 50) throw new TimeoutException("Pickup check exceeded 50 seconds.");
                if (phase == 0)
                {
                    if (now - phaseStarted < 2) return;
                    spawner = MainPlayerIntegration.InScene<PickupSpawner>().Single();
                    player = MainPlayerIntegration.InScene<PlayerController>().Single();
                    controller = player.GetComponent<CharacterController>();
                    markers = FindMarkers();
                    Check(markers.Length == 4, "Four original pickup markers retained");
                    foreach (var marker in markers)
                    {
                        bool floor = Physics.Raycast(marker.position + Vector3.up, Vector3.down, out var hit, 10, ~0, QueryTriggerInteraction.Ignore);
                        Check(floor && Vector3.Dot(hit.normal, Vector3.up) > .95f &&
                            !Physics.CheckBox(hit.point + Vector3.up * .65f, new Vector3(.55f, .6f, .55f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore),
                            marker.name + " has floor support and clear pickup/approach space");
                    }
                    Check(spawner.ActiveCount == 2 && LivePickups().Length == 2, "Exactly two pickups spawn on scene start");
                    CheckPlacements();
                    player.enabled = false;
                    BeginApproach();
                    Next();
                }
                else if (phase == 1 || phase == 3)
                {
                    if (!collected)
                    {
                        Vector3 delta = targetPosition - player.transform.position; delta.y = 0;
                        controller.Move(Vector3.ClampMagnitude(delta, .12f) + Vector3.down * .01f);
                        return;
                    }
                    var inventory = player.GetComponent<PlayerInventory>();
                    Check(inventory.GetCount(ItemType.Medkit) > medkitsBefore || inventory.GetCount(ItemType.Shield) > shieldsBefore ||
                        (!shieldBefore && player.GetComponent<PlayerShield>().IsActive), "Trigger collection applies a shared inventory/shield effect");
                    Check(spawner.ActiveCount == 1, "Collection frees exactly one occupied marker");
                    Teleport(new Vector3(0, .12f, -24));
                    if (phase == 3)
                    {
                        Check(GameManager.Instance.BeginWave(1) && spawner.ActiveCount == 2, "Real GameManager WaveStarted event immediately refills the vacancy");
                    }
                    Next();
                }
                else if (phase == 2)
                {
                    float elapsed = Time.time - collectedAt;
                    if (!earlyChecked && elapsed >= 2)
                    { Check(spawner.ActiveCount == 1, "Replacement is delayed, not immediate"); earlyChecked = true; }
                    if (spawner.ActiveCount < 2) return;
                    Check(elapsed >= 24.5f && elapsed < 30, "Replacement respects the real 25-second delay (actual " + elapsed.ToString("F1") + " s)");
                    CheckPlacements();
                    BeginApproach();
                    Next();
                }
                else if (phase == 4 && now - phaseStarted > .3)
                {
                    Check(LivePickups().Length == 2, "Wave refill leaves two live pickups, no immediate duplicate");
                    CheckPlacements();
                    Finish();
                }
            }
            catch (Exception exception) { Check(false, exception.ToString()); Finish(); }
        }

        private static void BeginApproach()
        {
            target = LivePickups().First();
            targetPosition = target.transform.position;
            collected = false;
            var inventory = player.GetComponent<PlayerInventory>();
            medkitsBefore = inventory.GetCount(ItemType.Medkit);
            shieldsBefore = inventory.GetCount(ItemType.Shield);
            shieldBefore = player.GetComponent<PlayerShield>().IsActive;
            target.Collected += OnCollected;
            Report.Add("SAMPLE: Approach " + target.GetType().Name + " at " + targetPosition.ToString("F2"));
            Teleport(targetPosition + Vector3.forward * 1.5f + Vector3.up * .1f);
        }

        private static void OnCollected(Pickup pickup) { collected = true; collectedAt = Time.time; }
        private static Pickup[] LivePickups() => spawner.GetComponentsInChildren<Pickup>();
        private static void Next() { phase++; phaseStarted = EditorApplication.timeSinceStartup; }
        private static void Teleport(Vector3 position)
        {
            controller.enabled = false; player.transform.position = position; controller.enabled = true;
            Physics.SyncTransforms();
        }

        private static void CheckPlacements()
        {
            var pickups = LivePickups();
            var occupied = new HashSet<Transform>();
            bool ok = pickups.Length == 2;
            foreach (var pickup in pickups)
            {
                var marker = markers.FirstOrDefault(point => Vector2.Distance(new Vector2(point.position.x, point.position.z),
                    new Vector2(pickup.transform.position.x, pickup.transform.position.z)) < .01f);
                ok &= marker != null && occupied.Add(marker) && pickup.GetComponent<Collider>().isTrigger &&
                    Physics.Raycast(pickup.transform.position + Vector3.up * .1f, Vector3.down, out var hit, .2f, ~0, QueryTriggerInteraction.Ignore) &&
                    Mathf.Abs(hit.point.y - pickup.transform.position.y) < .02f;
            }
            Check(ok, "Pickups occupy distinct original markers, sit on solid floor and use non-blocking triggers");
        }

        private static Transform[] FindMarkers() => Enumerable.Range(0, 4)
            .Select(i => GameObject.Find("GameplayMarkers/PickupPoints/Pickup_0" + i)?.transform).Where(point => point != null).ToArray();
        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(bool exitPlay = true)
        {
            running = false; EditorApplication.update -= Tick;
            if (target != null) target.Collected -= OnCollected;
            if (player != null) player.enabled = true;
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllLines(ReportPath, Report);
            Debug.Log("[MainPickups] Smoke check finished. Failures: " + failures + "; " + ReportPath);
            if (exitPlay) EditorApplication.isPlaying = false;
        }
        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
        }
    }
}
