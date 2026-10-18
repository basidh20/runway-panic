using System.IO;
using RunwayPanic.Data;
using RunwayPanic.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.Core.EditorTools
{
    /// <summary>
    /// Tools > S2 > Build Waves and Wave HUD, re-runnably:
    /// <list type="number">
    /// <item>writes the four wave assets in <c>ScriptableObjects/Waves</c> (Gulls → Pigeons → Crows → Hawks; existing
    /// assets keep their GUIDs and are overwritten with the values below);</item>
    /// <item>puts a <see cref="WaveManager"/> with the waves on <c>P_GameManager</c> (already in Main: no scene edit);</item>
    /// <item>adds the wave timer, birds-left counter and centre banner to the HUD on <c>P_Player_Gameplay</c>.</item>
    /// </list>
    /// Balance: killing every bird pays for the next wave's gun with coins to spare for a medkit; killing ~60% still
    /// affords it (see <c>Docs/SystemsEngineer/WaveBalanceReport.md</c>, written by the playtest).
    /// </summary>
    public static class WaveSetup
    {
        private const string WaveFolder = "Assets/_Project/ScriptableObjects/Waves";
        private const string BirdFolder = "Assets/_Project/ScriptableObjects/Birds";
        private const string GameManagerPrefab = "Assets/_Project/Prefabs/Core/P_GameManager.prefab";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab";
        private const string RoundedSprite = "Assets/_Project/Art/UI/Sprites/T_UI_Rounded.png";

        private struct WaveSpec
        {
            public string File, Title, Hint;
            public float Duration, Interval, FirstDelay;
            public int MaxAlive, Bonus;
            public (string species, int count)[] Groups;
        }

        private static readonly WaveSpec[] Specs =
        {
            new WaveSpec
            {
                File = "Wave_1_Gulls", Title = "Gulls", Hint = "The pistol works best on gulls.",
                Duration = 90f, Interval = 3.5f, FirstDelay = 1.5f, MaxAlive = 4, Bonus = 10,
                Groups = new[] { ("Gull", 9) },
            },
            new WaveSpec
            {
                File = "Wave_2_Pigeons", Title = "Pigeons",
                Hint = "Buy the Assault Rifle for pigeons. Switch guns with 1 / 2.",
                Duration = 100f, Interval = 2.5f, FirstDelay = 1.5f, MaxAlive = 6, Bonus = 15,
                Groups = new[] { ("Pigeon", 10), ("Gull", 3) },
            },
            new WaveSpec
            {
                File = "Wave_3_Crows", Title = "Crows", Hint = "One sniper shot drops a crow. Guard your pickups!",
                Duration = 110f, Interval = 3.5f, FirstDelay = 1.5f, MaxAlive = 6, Bonus = 20,
                Groups = new[] { ("Crow", 8), ("Pigeon", 5) },
            },
            new WaveSpec
            {
                File = "Wave_4_Hawks", Title = "Hawks", Hint = "Only the RPG stops a hawk. When it screams, look up!",
                Duration = 120f, Interval = 5f, FirstDelay = 2f, MaxAlive = 5, Bonus = 0,
                Groups = new[] { ("Hawk", 3), ("Crow", 3), ("Gull", 3) },
            },
        };

        [MenuItem("Tools/S2/Build Waves and Wave HUD")]
        public static void Build()
        {
            WaveData[] waves = BuildWaveAssets();
            AddWaveManager(waves);
            AddWaveHud();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Waves] Built {waves.Length} waves, the WaveManager on P_GameManager and the wave HUD.");
        }

        private static WaveData[] BuildWaveAssets()
        {
            Directory.CreateDirectory(WaveFolder);
            var waves = new WaveData[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                WaveSpec spec = Specs[i];
                string path = $"{WaveFolder}/{spec.File}.asset";
                var wave = AssetDatabase.LoadAssetAtPath<WaveData>(path);
                if (wave == null)
                {
                    wave = ScriptableObject.CreateInstance<WaveData>();
                    AssetDatabase.CreateAsset(wave, path);
                }

                var so = new SerializedObject(wave);
                so.FindProperty("waveNumber").intValue = i + 1;
                so.FindProperty("title").stringValue = spec.Title;
                so.FindProperty("hint").stringValue = spec.Hint;
                so.FindProperty("duration").floatValue = spec.Duration;
                so.FindProperty("spawnInterval").floatValue = spec.Interval;
                so.FindProperty("firstSpawnDelay").floatValue = spec.FirstDelay;
                so.FindProperty("maxAlive").intValue = spec.MaxAlive;
                so.FindProperty("clearBonus").intValue = spec.Bonus;
                SerializedProperty groups = so.FindProperty("spawnGroups");
                groups.arraySize = spec.Groups.Length;
                for (int g = 0; g < spec.Groups.Length; g++)
                {
                    SerializedProperty group = groups.GetArrayElementAtIndex(g);
                    string speciesPath = $"{BirdFolder}/BirdSpecies_{spec.Groups[g].species}.asset";
                    group.FindPropertyRelative("species").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<BirdSpeciesData>(speciesPath);
                    group.FindPropertyRelative("count").intValue = spec.Groups[g].count;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(wave);
                waves[i] = wave;
            }

            return waves;
        }

        private static void AddWaveManager(WaveData[] waves)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GameManagerPrefab);
            try
            {
                if (!root.TryGetComponent(out WaveManager manager))
                    manager = root.AddComponent<WaveManager>();

                var so = new SerializedObject(manager);
                SerializedProperty list = so.FindProperty("waves");
                list.arraySize = waves.Length;
                for (int i = 0; i < waves.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = waves[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                if (root.TryGetComponent(out GameManager game) && game.TotalWaves != waves.Length)
                    Debug.LogWarning(
                        $"[Waves] GameManager Total Waves is {game.TotalWaves}, but {waves.Length} waves were built.");
                PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddWaveHud()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var hud = root.GetComponentInChildren<HudUI>(true);
                Transform canvas = hud.transform;
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                // Under the wave number (top centre), and top right.
                Text timer = EnsureText(canvas, "WaveTimerText", font, 46, TextAnchor.UpperCenter,
                    new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(300f, 60f));
                Text birdsLeft = EnsureText(canvas, "BirdsLeftText", font, 28, TextAnchor.UpperRight,
                    new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(420f, 44f));

                // The banner: a dark rounded box in the upper middle with a title and one or two lines below.
                Transform bannerTransform = canvas.Find("WaveBanner");
                GameObject banner = bannerTransform != null
                    ? bannerTransform.gameObject
                    : new GameObject("WaveBanner", typeof(RectTransform), typeof(Image));
                banner.transform.SetParent(canvas, false);
                banner.layer = canvas.gameObject.layer;
                var rect = (RectTransform)banner.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 230f);
                rect.sizeDelta = new Vector2(980f, 190f);
                var image = banner.GetComponent<Image>();
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSprite);
                image.type = Image.Type.Sliced;
                image.color = new Color(0.05f, 0.06f, 0.08f, 0.82f);
                image.raycastTarget = false;
                Text title = EnsureText(banner.transform, "Title", font, 54, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(940f, 70f));
                title.color = new Color(1f, 0.62f, 0.22f);
                Text subtitle = EnsureText(banner.transform, "Subtitle", font, 28, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -38f), new Vector2(940f, 90f));
                subtitle.fontStyle = FontStyle.Normal;
                subtitle.color = new Color(0.88f, 0.9f, 0.92f);
                banner.SetActive(false);

                var so = new SerializedObject(hud);
                so.FindProperty("waveTimerText").objectReferenceValue = timer;
                so.FindProperty("birdsLeftText").objectReferenceValue = birdsLeft;
                so.FindProperty("banner").objectReferenceValue = banner;
                so.FindProperty("bannerTitle").objectReferenceValue = title;
                so.FindProperty("bannerSubtitle").objectReferenceValue = subtitle;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Text EnsureText(Transform parent, string name, Font font, int size, TextAnchor alignment,
            Vector2 anchor, Vector2 position, Vector2 box)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = box;

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            if (go.GetComponent<Shadow>() == null)
            {
                var shadow = go.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
                shadow.effectDistance = new Vector2(2f, -2f);
            }
            return text;
        }
    }
}
