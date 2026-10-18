using System;
using System.Linq;
using RunwayPanic.AI;
using RunwayPanic.Core;
using RunwayPanic.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Agents.Pigeon.EditorTools
{
    /// <summary>
    /// One-time P1 setup, <b>Tools > IS > Pigeon > Set Up Pigeon Sandbox and Prefab</b>:
    /// <list type="number">
    /// <item>creates <c>PigeonStrategyConfig.asset</c> (or reuses an existing one),</item>
    /// <item>adds <see cref="PigeonBrain"/> to <c>P_Bird_Pigeon</c> only (never <c>P_Bird_Base</c>, never another species),</item>
    /// <item>copies <c>Main</c> into <c>Sandbox_IS_Pigeon</c>, switches the production WaveManager off there, and adds
    /// the test spawners (<b>B</b> = 6 pigeons, <b>N</b> = 1 late joiner) and the sandbox keys (<see cref="PigeonSandboxControls"/>).</item>
    /// </list>
    /// It never overwrites: an existing sandbox stops it, a prefab that already has a <see cref="PigeonBrain"/> is left
    /// as it is, and a prefab with any other brain stops it. <c>Main</c> and the Build Settings are never changed.
    /// </summary>
    public static class PigeonSandboxSetup
    {
        public const string SandboxPath = "Assets/_Project/Scenes/Sandbox/Sandbox_IS_Pigeon.unity";
        public const string ConfigPath = "Assets/_Project/ScriptableObjects/Birds/Strategy/PigeonStrategyConfig.asset";
        public const string PrefabPath = "Assets/_Project/Prefabs/Birds/P_Bird_Pigeon.prefab";
        public const string MainPath = "Assets/_Project/Scenes/Main.unity";
        public const string CounterWeaponPath = "Assets/_Project/ScriptableObjects/Weapons/Weapon_Tier2_AssaultRifle.asset";
        public const string ToolsRootName = "PigeonSandboxTools";

        private const string RoostGroup = "PigeonRoost";
        private const int ExpectedRoosts = 3;
        // Wave 2's maxAlive: the biggest flock the real game can produce.
        private const int FlockSpawnCount = 6;

        [MenuItem("Tools/IS/Pigeon/Set Up Pigeon Sandbox and Prefab")]
        public static void SetUp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode first.");
            if (SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save or discard the open scene first: this tool opens the new sandbox in its place.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxPath) != null)
                throw new InvalidOperationException($"{SandboxPath} already exists. This one-time setup never overwrites it; delete it first to rebuild it.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainPath) == null)
                throw new InvalidOperationException($"{MainPath} not found.");

            WeaponData counterWeapon = AssetDatabase.LoadAssetAtPath<WeaponData>(CounterWeaponPath);
            if (counterWeapon == null || counterWeapon.TargetSpecies != BirdSpecies.Pigeon)
                throw new InvalidOperationException($"{CounterWeaponPath} should be the gun whose target species is Pigeon.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            BirdHealth health = prefab != null ? prefab.GetComponent<BirdHealth>() : null;
            if (health == null || prefab.GetComponent<IBirdMotor>() == null || health.Species == null ||
                health.Species.Species != BirdSpecies.Pigeon)
                throw new InvalidOperationException($"{PrefabPath} should be the pigeon variant with BirdHealth (Pigeon species) and a motor.");

            BirdBrain[] brains = prefab.GetComponents<BirdBrain>();
            if (brains.Length > 1 || (brains.Length == 1 && !(brains[0] is PigeonBrain)))
                throw new InvalidOperationException($"{PrefabPath} already has another brain ({brains[0].GetType().Name}); refusing to change it.");

            PigeonStrategyConfig config = LoadOrCreateConfig();
            if (brains.Length == 0)
                AddBrain(config);
            else
                Debug.Log($"[PigeonSetup] {PrefabPath} already has a {nameof(PigeonBrain)}; left unchanged.");

            GameObject tools = CreateSandbox(prefab, counterWeapon);
            Selection.activeGameObject = tools;
            Debug.Log("[PigeonSetup] Sandbox ready. Play, then B spawns 6 pigeons, N one late joiner, G gives the assault " +
                      "rifle, O toggles the shop, F3 toggles state labels. Production waves are off in this sandbox; Main is unchanged.");
        }

        private static PigeonStrategyConfig LoadOrCreateConfig()
        {
            PigeonStrategyConfig config = AssetDatabase.LoadAssetAtPath<PigeonStrategyConfig>(ConfigPath);
            if (config != null)
                return config;

            config = ScriptableObject.CreateInstance<PigeonStrategyConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        // Edits the variant's own contents, so the brain is an added component on P_Bird_Pigeon and P_Bird_Base
        // (shared by the gull and the crow) is not touched.
        private static void AddBrain(PigeonStrategyConfig config)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                PigeonBrain brain = contents.AddComponent<PigeonBrain>();
                var data = new SerializedObject(brain);
                data.FindProperty("config").objectReferenceValue = config;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static GameObject CreateSandbox(GameObject prefab, WeaponData counterWeapon)
        {
            if (!AssetDatabase.CopyAsset(MainPath, SandboxPath))
                throw new InvalidOperationException("Could not copy Main into a new pigeon sandbox.");

            Scene scene = EditorSceneManager.OpenScene(SandboxPath, OpenSceneMode.Single);

            FlightGrid[] grids = UnityEngine.Object.FindObjectsByType<FlightGrid>(FindObjectsInactive.Include);
            int roosts = UnityEngine.Object.FindObjectsByType<BirdPoint>(FindObjectsInactive.Include)
                .Count(point => point.Type == BirdPointType.Roost && point.Group == RoostGroup);
            WaveManager[] waves = UnityEngine.Object.FindObjectsByType<WaveManager>(FindObjectsInactive.Include);
            if (grids.Length != 1 || roosts != ExpectedRoosts || waves.Length != 1)
                throw new InvalidOperationException(
                    $"The copied scene has {grids.Length} flight grid(s), {roosts} pigeon roost(s) and {waves.Length} WaveManager(s); " +
                    $"expected 1, {ExpectedRoosts} and 1. Main is unchanged; review and delete {SandboxPath} before running again.");

            // Production waves would start wave 1 (gulls) six seconds into Play, and their time-up cleanup would
            // destroy the test pigeons. Switched off in the sandbox copy only (an override on the GameManager prefab
            // instance), the same way the Gull sandbox isolates itself.
            var waveData = new SerializedObject(waves[0]);
            waveData.FindProperty("m_Enabled").boolValue = false;
            waveData.ApplyModifiedPropertiesWithoutUndo();

            var tools = new GameObject(ToolsRootName);
            tools.transform.position = new Vector3(0f, 8f, 0f); // spawner fallback if a scene has no Spawn markers
            AddSpawner(tools.transform, "PigeonSpawner_Flock (B)", prefab, FlockSpawnCount, Key.B);
            AddSpawner(tools.transform, "PigeonSpawner_LateJoiner (N)", prefab, 1, Key.N);

            PigeonSandboxControls controls = tools.AddComponent<PigeonSandboxControls>();
            var controlData = new SerializedObject(controls);
            controlData.FindProperty("counterWeapon").objectReferenceValue = counterWeapon;
            controlData.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save the pigeon sandbox.");
            return tools;
        }

        // The shared spawner places birds at random Spawn markers, as the WaveManager does, so arrivals come from
        // scattered directions just like in a real wave.
        private static void AddSpawner(Transform parent, string objectName, GameObject prefab, int count, Key key)
        {
            var spawnerObject = new GameObject(objectName);
            spawnerObject.transform.SetParent(parent, false);

            BirdTestSpawner spawner = spawnerObject.AddComponent<BirdTestSpawner>();
            var data = new SerializedObject(spawner);
            data.FindProperty("prefab").objectReferenceValue = prefab;
            data.FindProperty("count").intValue = count;
            data.FindProperty("spawnKey").intValue = (int)key;
            data.FindProperty("spawnOnStart").boolValue = false;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
