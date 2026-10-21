using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Audio;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RunwayPanic.AudioTools
{
    /// <summary>
    /// Tools > Audio > Generate Game Audio: builds the game's whole soundscape in one go, re-runnably:
    /// <list type="number">
    /// <item>synthesises every clip (<see cref="GameAudioRecipes"/>) into <c>Audio/SFX</c>, <c>Audio/Music</c> and
    /// <c>Audio/Ambience</c>;</item>
    /// <item>applies import settings (short effects decompressed, loops uncompressed so they never click, the long
    /// environment loops streamed);</item>
    /// <item>creates the <see cref="SoundCue"/> assets in <c>Audio/Cues</c> (existing ones are updated, keeping GUIDs);</item>
    /// <item>wires the sound components onto the player, GameManager, door, barricade and bird prefabs, and the hawk's
    /// scream onto <c>P_Bird_Hawk</c>.</item>
    /// </list>
    /// Replace any generated clip with a recorded one by overwriting the .wav (same name) — or point a cue at new
    /// clips — and do not run this tool again afterwards for that clip.
    /// </summary>
    public static class GameAudioGenerator
    {
        private const string Root = GameAudioRecipes.Root;
        private const string CueFolder = Root + "/Cues";
        private const string CrowCaw = Root + "/SFX/Crow/SFX_Crow_Caw.wav";

        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab";
        private const string GameManagerPrefab = "Assets/_Project/Prefabs/Core/P_GameManager.prefab";
        private const string DoorPrefab = "Assets/_Project/Prefabs/Interactables/P_Door_Shelter.prefab";
        private const string BarricadePrefab = "Assets/_Project/Prefabs/Interactables/P_Barricade_Movable.prefab";
        private const string BirdBasePrefab = "Assets/_Project/Prefabs/Birds/P_Bird_Base.prefab";
        private const string HawkPrefab = "Assets/_Project/Prefabs/Birds/P_Bird_Hawk.prefab";

        [MenuItem("Tools/Audio/Generate Game Audio")]
        public static void Generate()
        {
            int written = GenerateClips(_ => true);
            WirePrefabs();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Audio] Generated {written} clips, {CueCount} cues, and wired the prefabs.");
        }

        /// <summary>
        /// Synthesises only the clips whose file name contains one of <paramref name="names"/>, then refreshes every
        /// cue — for a feature that adds a few sounds without rewriting the rest. Prefabs are not touched.
        /// </summary>
        internal static int GenerateOnly(params string[] names) =>
            GenerateClips(path => names.Any(n => Path.GetFileName(path).Contains(n)));

        private static int GenerateClips(Func<string, bool> include)
        {
            var kinds = new Dictionary<string, ClipKind>();
            int written = 0;
            try
            {
                List<(string path, ClipKind kind, Func<Signal> make)> recipes =
                    GameAudioRecipes.All().Where(r => include(r.path)).ToList();
                foreach ((string path, ClipKind kind, Func<Signal> make) in recipes)
                {
                    EditorUtility.DisplayProgressBar("Generating game audio", Path.GetFileName(path), written / (float)recipes.Count);
                    make().Save(path);
                    kinds[path] = kind;
                    written++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            ApplyImportSettings(kinds);
            CreateCues();
            AssetDatabase.SaveAssets();
            return written;
        }

        // ---------- Import settings ----------

        private static void ApplyImportSettings(Dictionary<string, ClipKind> kinds)
        {
            foreach (KeyValuePair<string, ClipKind> entry in kinds)
            {
                if (!(AssetImporter.GetAtPath(entry.Key) is AudioImporter importer))
                    continue;

                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                switch (entry.Value)
                {
                    case ClipKind.Effect:
                        settings.loadType = AudioClipLoadType.DecompressOnLoad;
                        settings.compressionFormat = AudioCompressionFormat.ADPCM;
                        break;
                    case ClipKind.Loop: // uncompressed, so the loop point never clicks
                        settings.loadType = AudioClipLoadType.DecompressOnLoad;
                        settings.compressionFormat = AudioCompressionFormat.PCM;
                        break;
                    case ClipKind.Music: // in memory (not streamed), so the layers start together and stay in step
                        settings.loadType = AudioClipLoadType.CompressedInMemory;
                        settings.compressionFormat = AudioCompressionFormat.Vorbis;
                        settings.quality = 0.5f;
                        break;
                    case ClipKind.Ambience:
                        settings.loadType = AudioClipLoadType.Streaming;
                        settings.compressionFormat = AudioCompressionFormat.Vorbis;
                        settings.quality = 0.6f;
                        break;
                }

                importer.defaultSampleSettings = settings;
                importer.forceToMono = true;
                importer.loadInBackground = entry.Value == ClipKind.Ambience || entry.Value == ClipKind.Music;
                importer.SaveAndReimport();
            }
        }

        // ---------- Cues ----------

        private struct CueSpec
        {
            public string Name;
            public string[] Clips; // asset paths
            public SoundCategory Category;
            public float Volume;
            public Vector2 Pitch;
            public bool Spatial;
            public float Min, Max;
            public int MaxInstances;
        }

        private static int CueCount => Specs().Count();

        private static string S(string folder, string name) => $"{Root}/SFX/{folder}/{name}.wav";

        private static string Amb(string name) => $"{Root}/Ambience/{name}.wav";

        private static string[] Variants(string folder, string name, int count) =>
            Enumerable.Range(1, count).Select(i => S(folder, $"{name}_{i}")).ToArray();

        private static CueSpec Cue(string name, string[] clips, SoundCategory category, float volume, float pitchSpread,
            bool spatial, float min = 3f, float max = 50f, int maxInstances = 4) => new CueSpec
        {
            Name = name, Clips = clips, Category = category, Volume = volume,
            Pitch = new Vector2(1f - pitchSpread, 1f + pitchSpread), Spatial = spatial, Min = min, Max = max,
            MaxInstances = maxInstances,
        };

        private static IEnumerable<CueSpec> Specs()
        {
            const SoundCategory W = SoundCategory.Weapons, P = SoundCategory.Player, B = SoundCategory.Birds;
            const SoundCategory Wd = SoundCategory.World, U = SoundCategory.UI;

            // Weapons: heard far; fast guns allow more overlapping shots.
            yield return Cue("SC_Shot_Pistol", Variants("Weapons", "SFX_Shot_Pistol", 2), W, 0.85f, 0.04f, true, 4f, 120f, 4);
            yield return Cue("SC_Shot_Rifle", Variants("Weapons", "SFX_Shot_Rifle", 3), W, 0.75f, 0.03f, true, 4f, 120f, 6);
            yield return Cue("SC_Shot_Sniper", new[] { S("Weapons", "SFX_Shot_Sniper") }, W, 1f, 0.02f, true, 6f, 200f, 2);
            yield return Cue("SC_Shot_RPG", new[] { S("Weapons", "SFX_Shot_RPG") }, W, 1f, 0.03f, true, 6f, 160f, 2);
            yield return Cue("SC_Rocket_Flight", new[] { S("Weapons", "SFX_Rocket_Flight") }, W, 0.55f, 0f, true, 3f, 80f, 3);
            yield return Cue("SC_Explosion", Variants("Weapons", "SFX_Explosion", 2), W, 1f, 0.06f, true, 12f, 300f, 3);
            yield return Cue("SC_Reload_Start", new[] { S("Weapons", "SFX_Reload_Start") }, W, 0.6f, 0.02f, true, 2f, 20f, 2);
            yield return Cue("SC_Reload_Finish", new[] { S("Weapons", "SFX_Reload_Finish") }, W, 0.65f, 0.02f, true, 2f, 20f, 2);
            yield return Cue("SC_Weapon_Equip", new[] { S("Weapons", "SFX_Weapon_Equip") }, W, 0.55f, 0.03f, true, 2f, 15f, 2);
            yield return Cue("SC_Bullet_Impact", Variants("Weapons", "SFX_Bullet_Impact", 3), W, 0.5f, 0.1f, true, 2f, 35f, 6);

            // Player: footsteps in the world; the player's own body sounds 2D (always clear).
            yield return Cue("SC_Footstep_Walk", Variants("Player", "SFX_Footstep_Walk", 4), P, 0.35f, 0.08f, true, 5f, 20f, 3);
            yield return Cue("SC_Footstep_Run", Variants("Player", "SFX_Footstep_Run", 4), P, 0.45f, 0.08f, true, 5f, 25f, 3);
            yield return Cue("SC_Jump", Variants("Player", "SFX_Jump", 3), P, 0.4f, 0.06f, false, maxInstances: 1);
            yield return Cue("SC_Land", new[] { S("Player", "SFX_Land") }, P, 0.7f, 0.05f, false);
            yield return Cue("SC_Player_Hurt", Variants("Player", "SFX_Player_Hurt", 3), P, 0.7f, 0.05f, false, maxInstances: 1);
            yield return Cue("SC_Player_Death", new[] { S("Player", "SFX_Player_Death") }, P, 0.8f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Respawn", new[] { S("Player", "SFX_Respawn") }, P, 0.6f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Heartbeat", new[] { S("Player", "SFX_Heartbeat") }, P, 0.5f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Medkit", new[] { S("Player", "SFX_Medkit") }, P, 0.6f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Shield_On", new[] { S("Player", "SFX_Shield_On") }, P, 0.6f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Shield_Hum", new[] { S("Player", "SFX_Shield_Hum") }, P, 0.25f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Shield_Off", new[] { S("Player", "SFX_Shield_Off") }, P, 0.55f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Pickup", new[] { S("Player", "SFX_Pickup") }, P, 0.5f, 0.03f, false, maxInstances: 2);

            // World.
            yield return Cue("SC_Door_Open", new[] { S("World", "SFX_Door_Open") }, Wd, 0.7f, 0.05f, true, 2f, 30f, 2);
            yield return Cue("SC_Door_Close", new[] { S("World", "SFX_Door_Close") }, Wd, 0.8f, 0.05f, true, 2f, 35f, 2);
            yield return Cue("SC_Barricade_Scrape", new[] { S("World", "SFX_Barricade_Scrape") }, Wd, 0.5f, 0f, true, 2f, 30f, 2);
            yield return Cue("SC_Barricade_Settle", new[] { S("World", "SFX_Barricade_Settle") }, Wd, 0.8f, 0.05f, true, 2f, 35f, 2);

            // UI and game (2D).
            yield return Cue("SC_UI_Click", new[] { S("UI", "SFX_UI_Click") }, U, 0.4f, 0f, false, maxInstances: 3);
            yield return Cue("SC_Coin", new[] { S("UI", "SFX_Coin") }, U, 0.45f, 0.03f, false, maxInstances: 4);
            yield return Cue("SC_Purchase", new[] { S("UI", "SFX_Purchase") }, U, 0.55f, 0f, false, maxInstances: 2);
            yield return Cue("SC_Purchase_Denied", new[] { S("UI", "SFX_Purchase_Denied") }, U, 0.5f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Wave_Start", new[] { S("UI", "SFX_Wave_Start") }, U, 0.6f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Wave_Cleared", new[] { S("UI", "SFX_Wave_Cleared") }, U, 0.6f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Victory", new[] { S("UI", "SFX_Victory") }, U, 0.8f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Game_Over", new[] { S("UI", "SFX_Game_Over") }, U, 0.9f, 0f, false, maxInstances: 1);

            // Birds: many at once, so flaps are quiet and capped.
            yield return Cue("SC_Bird_Flap", Variants("Birds", "SFX_Bird_Flap", 3), B, 0.3f, 0.15f, true, 2f, 25f, 10);
            yield return Cue("SC_Bird_Dive", new[] { S("Birds", "SFX_Bird_Dive") }, B, 0.6f, 0.05f, true, 3f, 60f, 3);
            yield return Cue("SC_Bird_Hit", Variants("Birds", "SFX_Bird_Hit", 3), B, 0.6f, 0.1f, true, 3f, 45f, 4);
            yield return Cue("SC_Bird_Death", new[] { S("Birds", "SFX_Bird_Death") }, B, 0.7f, 0.1f, true, 3f, 50f, 4);
            yield return Cue("SC_Gull_Call", Variants("Birds", "SFX_Gull_Call", 2), B, 0.55f, 0.08f, true, 4f, 70f, 3);
            yield return Cue("SC_Pigeon_Coo", Variants("Birds", "SFX_Pigeon_Coo", 2), B, 0.5f, 0.08f, true, 3f, 40f, 3);
            yield return Cue("SC_Crow_Idle", new[] { CrowCaw }, B, 0.45f, 0.1f, true, 4f, 60f, 3);

            // Environment: two 2D beds, a very quiet music pad, and distant one-off events placed around the listener.
            const SoundCategory A = SoundCategory.Ambience;
            yield return Cue("SC_Ambience_Airport", new[] { Amb("AMB_Airport") }, A, 0.15f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Ambience_Wind", new[] { Amb("AMB_Wind") }, A, 0.06f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Music_Ambient", new[] { $"{Root}/Music/MUS_Ambient.wav" }, SoundCategory.Music, 0.08f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Music_Tension_1", new[] { $"{Root}/Music/MUS_Tension_1.wav" }, SoundCategory.Music, 0.14f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Music_Tension_2", new[] { $"{Root}/Music/MUS_Tension_2.wav" }, SoundCategory.Music, 0.12f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Music_Tension_3", new[] { $"{Root}/Music/MUS_Tension_3.wav" }, SoundCategory.Music, 0.18f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Music_Tension_4", new[] { $"{Root}/Music/MUS_Tension_4.wav" }, SoundCategory.Music, 0.2f, 0f, false, maxInstances: 1);
            yield return Cue("SC_Amb_Jet_Flyover", new[] { Amb("SFX_Amb_Jet_Flyover") }, A, 0.12f, 0.08f, false, maxInstances: 1);
            yield return Cue("SC_Amb_PA_Chime", new[] { Amb("SFX_Amb_PA_Chime") }, A, 0.1f, 0f, true, 20f, 150f, 1);
            yield return Cue("SC_Amb_Gull_Far", new[] { Amb("SFX_Amb_Gull_Far_1"), Amb("SFX_Amb_Gull_Far_2") }, A, 0.12f, 0.1f, true, 15f, 120f, 2);
            yield return Cue("SC_Amb_Fence_Rattle", new[] { Amb("SFX_Amb_Fence_Rattle") }, A, 0.08f, 0.1f, true, 8f, 80f, 1);
        }

        private static void CreateCues()
        {
            Directory.CreateDirectory(CueFolder);
            foreach (CueSpec spec in Specs())
            {
                string path = $"{CueFolder}/{spec.Name}.asset";
                var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
                if (cue == null)
                {
                    cue = ScriptableObject.CreateInstance<SoundCue>();
                    AssetDatabase.CreateAsset(cue, path);
                }

                var so = new SerializedObject(cue);
                SerializedProperty clips = so.FindProperty("clips");
                clips.arraySize = spec.Clips.Length;
                for (int i = 0; i < spec.Clips.Length; i++)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.Clips[i]);
                    if (clip == null)
                        Debug.LogWarning($"[Audio] {spec.Name}: clip not found at {spec.Clips[i]}.");
                    clips.GetArrayElementAtIndex(i).objectReferenceValue = clip;
                }

                so.FindProperty("category").enumValueIndex = (int)spec.Category;
                so.FindProperty("volume").floatValue = spec.Volume;
                so.FindProperty("pitchRange").vector2Value = spec.Pitch;
                so.FindProperty("spatial").boolValue = spec.Spatial;
                so.FindProperty("minDistance").floatValue = spec.Min;
                so.FindProperty("maxDistance").floatValue = spec.Max;
                so.FindProperty("maxInstances").intValue = spec.MaxInstances;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(cue);
            }
        }

        private static SoundCue LoadCue(string name)
        {
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>($"{CueFolder}/{name}.asset");
            if (cue == null)
                Debug.LogWarning($"[Audio] Cue {name} is missing.");
            return cue;
        }

        // ---------- Prefabs ----------

        private static void WirePrefabs()
        {
            EditPrefab(PlayerPrefab, root =>
            {
                Set(Ensure<WeaponSounds>(root), ("shotByRarity", new object[] { "SC_Shot_Pistol", "SC_Shot_Rifle", "SC_Shot_Sniper", "SC_Shot_RPG" }),
                    ("reloadStart", "SC_Reload_Start"), ("reloadFinish", "SC_Reload_Finish"), ("equip", "SC_Weapon_Equip"),
                    ("impact", "SC_Bullet_Impact"));
                Set(Ensure<PlayerSounds>(root), ("footstepWalk", "SC_Footstep_Walk"), ("footstepRun", "SC_Footstep_Run"),
                    ("jump", "SC_Jump"), ("land", "SC_Land"), ("hurt", "SC_Player_Hurt"), ("death", "SC_Player_Death"),
                    ("respawn", "SC_Respawn"), ("heartbeat", "SC_Heartbeat"), ("medkit", "SC_Medkit"),
                    ("shieldOn", "SC_Shield_On"), ("shieldHum", "SC_Shield_Hum"), ("shieldOff", "SC_Shield_Off"),
                    ("pickup", "SC_Pickup"));
            });

            EditPrefab(GameManagerPrefab, root =>
            {
                Set(Ensure<GameSounds>(root), ("waveStart", "SC_Wave_Start"), ("waveCleared", "SC_Wave_Cleared"),
                    ("gameOver", "SC_Game_Over"), ("victory", "SC_Victory"), ("coin", "SC_Coin"),
                    ("purchase", "SC_Purchase"), ("purchaseDenied", "SC_Purchase_Denied"), ("buttonClick", "SC_UI_Click"));
                Set(Ensure<AmbienceLoop>(root), ("ambience", "SC_Ambience_Airport"), ("wind", "SC_Ambience_Wind"),
                    ("events", new object[] { "SC_Amb_Jet_Flyover", "SC_Amb_PA_Chime", "SC_Amb_Gull_Far", "SC_Amb_Fence_Rattle" }));
                Set(Ensure<WaveMusic>(root), ("pad", "SC_Music_Ambient"),
                    ("tension", new object[] { "SC_Music_Tension_1", "SC_Music_Tension_2", "SC_Music_Tension_3", "SC_Music_Tension_4" }));
            });

            EditPrefab(DoorPrefab, root =>
            {
                var door = root.GetComponentInChildren<RunwayPanic.Interaction.Door>(true);
                if (door != null)
                    Set(Ensure<DoorSounds>(door.gameObject), ("open", "SC_Door_Open"), ("close", "SC_Door_Close"));
            });

            EditPrefab(BarricadePrefab, root =>
            {
                var barricade = root.GetComponentInChildren<RunwayPanic.Interaction.Barricade>(true);
                if (barricade != null)
                    Set(Ensure<BarricadeSounds>(barricade.gameObject), ("scrape", "SC_Barricade_Scrape"), ("settle", "SC_Barricade_Settle"));
            });

            foreach (string bird in new[] { BirdBasePrefab, HawkPrefab })
            {
                EditPrefab(bird, root => Set(Ensure<BirdSounds>(root), ("flap", "SC_Bird_Flap"), ("dive", "SC_Bird_Dive"),
                    ("hit", "SC_Bird_Hit"), ("death", "SC_Bird_Death"), ("gullCall", "SC_Gull_Call"),
                    ("pigeonCall", "SC_Pigeon_Coo"), ("crowCall", "SC_Crow_Idle")));
            }

            // The hawk's scream (HawkBrain plays it at the top of every stoop): its clip and a 3D source.
            EditPrefab(HawkPrefab, root =>
            {
                var brain = root.GetComponent<RunwayPanic.Agents.Hawk.HawkBrain>();
                if (brain == null)
                    return;
                AudioSource source = root.GetComponent<AudioSource>();
                if (source == null)
                    source = root.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 6f;
                source.maxDistance = 120f;
                source.dopplerLevel = 0f;
                source.volume = 0.9f;

                var so = new SerializedObject(brain);
                so.FindProperty("screamClip").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>(S("Birds", "SFX_Hawk_Scream"));
                so.FindProperty("screamSource").objectReferenceValue = source;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        private static void EditPrefab(string path, Action<GameObject> edit)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[Audio] Prefab not found: {path}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T Ensure<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        // Sets cue fields by name; an object[] value fills an array field.
        private static void Set(Component component, params (string field, object cue)[] fields)
        {
            var so = new SerializedObject(component);
            foreach ((string field, object value) in fields)
            {
                SerializedProperty property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogWarning($"[Audio] {component.GetType().Name} has no field '{field}'.");
                    continue;
                }

                if (value is object[] names)
                {
                    property.arraySize = names.Length;
                    for (int i = 0; i < names.Length; i++)
                        property.GetArrayElementAtIndex(i).objectReferenceValue = LoadCue((string)names[i]);
                }
                else
                {
                    property.objectReferenceValue = LoadCue((string)value);
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
