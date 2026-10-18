using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RunwayPanic.UI.EditorTools
{
    /// <summary>
    /// Tools > S2 > Build Main Menu: builds the whole menu flow, re-runnably.
    /// <list type="number">
    /// <item>Generates the UI sprites (rounded panel, circle, bird silhouette, dusk sky) in <c>Art/UI/Sprites</c>.</item>
    /// <item>Builds <c>Prefabs/UI/P_SettingsPanel</c> (volumes, sensitivity, invert Y, fullscreen, V-Sync, quality).</item>
    /// <item>Builds <c>Scenes/MainMenu.unity</c>: animated dusk backdrop, title, Play / How to Play / Settings /
    /// Credits / Quit, quit confirmation and a loading screen with tips.</item>
    /// <item>Updates the Pause, Game Over and Victory screens on <c>P_GameManager</c>: Settings and Main Menu buttons,
    /// clearer highlight for keyboard and gamepad.</item>
    /// <item>Puts the menu first in the build's scene list, before <c>Main</c>.</item>
    /// </list>
    /// </summary>
    public static class MainMenuBuilder
    {
        // Not under Art/Textures: the art pipeline forces textures there to the Default type, and these are sprites.
        private const string TextureFolder = "Assets/_Project/Art/UI/Sprites";
        private const string SettingsPrefabPath = "Assets/_Project/Prefabs/UI/P_SettingsPanel.prefab";
        private const string MenuScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        private const string GameScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string GameManagerPrefab = "Assets/_Project/Prefabs/Core/P_GameManager.prefab";
        private const string CueFolder = "Assets/_Project/Audio/Cues";

        // The existing screens' palette.
        private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.11f, 1f);
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color Accent = new Color(0.85f, 0.45f, 0.12f, 1f);
        private static readonly Color AccentBright = new Color(1f, 0.6f, 0.22f, 1f);
        private static readonly Color AccentDark = new Color(0.65f, 0.32f, 0.08f, 1f);
        private static readonly Color ButtonGrey = new Color(0.3f, 0.34f, 0.38f, 1f);
        private static readonly Color TextLight = new Color(0.85f, 0.88f, 0.9f, 1f);
        private static readonly Color TextDim = new Color(0.62f, 0.67f, 0.72f, 1f);
        private static readonly Color TrackColor = new Color(0.16f, 0.19f, 0.22f, 1f);

        private static Font font;
        private static Sprite rounded;
        private static Sprite circle;
        private static Sprite bird;
        private static Sprite sky;

        [MenuItem("Tools/S2/Build Main Menu")]
        public static void Build()
        {
            // The menu scene is built in a fresh scene, which would drop unsaved edits in the open ones.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            MakeSprites();
            BuildSettingsPrefab();
            BuildMenuScene();
            UpdateGameScreens();
            UpdateBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[MainMenu] Built the main menu scene, the settings panel and the in-game screens; menu is first in the build.");
        }

        // =====================================================================
        // Sprites
        // =====================================================================

        private static void MakeSprites()
        {
            Directory.CreateDirectory(TextureFolder);
            WriteSprite("T_UI_Rounded", 64, 64, (x, y) => RoundedAlpha(x, y, 64, 64, 16f), new Vector4(20, 20, 20, 20));
            WriteSprite("T_UI_Circle", 64, 64, CircleAlpha, Vector4.zero);
            WriteSprite("T_UI_BirdSilhouette", 128, 64, BirdAlpha, Vector4.zero);
            WriteSprite("T_UI_DuskSky", 4, 256, null, Vector4.zero, SkyColor);
            LoadSprites();
        }

        // Opening a new scene unloads assets nothing references, so the sprites are (re)loaded before each use.
        private static void LoadSprites()
        {
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>($"{TextureFolder}/T_UI_Rounded.png");
            circle = AssetDatabase.LoadAssetAtPath<Sprite>($"{TextureFolder}/T_UI_Circle.png");
            bird = AssetDatabase.LoadAssetAtPath<Sprite>($"{TextureFolder}/T_UI_BirdSilhouette.png");
            sky = AssetDatabase.LoadAssetAtPath<Sprite>($"{TextureFolder}/T_UI_DuskSky.png");
        }

        private static SettingsPanelUI LoadSettingsPrefab() =>
            AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath).GetComponent<SettingsPanelUI>();

        private static float CircleAlpha(int x, int y) =>
            Mathf.Clamp01(31.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)));

        private static float RoundedAlpha(int x, int y, int w, int h, float r)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float dx = Mathf.Max(r - px, px - (w - r), 0f);
            float dy = Mathf.Max(r - py, py - (h - r), 0f);
            return Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
        }

        // A gull-like "M": two wings arched up from the body, thick at the shoulder and thin at the tips.
        private static float BirdAlpha(int x, int y)
        {
            var wing = new[] { new Vector2(64, 30), new Vector2(50, 38), new Vector2(34, 42), new Vector2(18, 36), new Vector2(4, 24) };
            var p = new Vector2(Mathf.Abs(x + 0.5f - 64f) + 64f, y + 0.5f); // mirror: both wings share one curve
            float best = 0f;
            for (int i = 0; i < wing.Length - 1; i++)
            {
                Vector2 a = wing[i], b = wing[i + 1];
                Vector2 ab = new Vector2(128f - b.x, b.y) - new Vector2(128f - a.x, a.y);
                Vector2 start = new Vector2(128f - a.x, a.y);
                float t = Mathf.Clamp01(Vector2.Dot(p - start, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(p, start + ab * t);
                float along = (i + t) / (wing.Length - 1);
                float thickness = Mathf.Lerp(6.5f, 1.2f, along);
                best = Mathf.Max(best, Mathf.Clamp01(thickness - d + 0.5f));
            }

            float body = Mathf.Clamp01(7f - Vector2.Distance(new Vector2(x + 0.5f, (y + 0.5f) * 1.6f), new Vector2(64, 30 * 1.6f)) + 0.5f);
            return Mathf.Max(best, body);
        }

        // Dusk: deep blue at the top, warm orange at the horizon.
        private static Color SkyColor(int x, int y)
        {
            float t = y / 255f;
            Color horizon = new Color(0.93f, 0.48f, 0.2f), middle = new Color(0.38f, 0.22f, 0.36f), top = new Color(0.05f, 0.07f, 0.16f);
            return t < 0.35f ? Color.Lerp(horizon, middle, t / 0.35f) : Color.Lerp(middle, top, (t - 0.35f) / 0.65f);
        }

        private static void WriteSprite(string name, int w, int h, System.Func<int, int, float> alpha, Vector4 border,
            System.Func<int, int, Color> color = null)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    texture.SetPixel(x, y, color != null ? color(x, y) : new Color(1f, 1f, 1f, alpha(x, y)));
            string path = $"{TextureFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // =====================================================================
        // Settings panel prefab
        // =====================================================================

        private static void BuildSettingsPrefab()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPrefabPath));
            var root = new GameObject("P_SettingsPanel", typeof(RectTransform));
            Stretch(root.GetComponent<RectTransform>());
            AddImage(root, DimColor, null); // blocks clicks behind it

            RectTransform box = Panel(root.transform, "Box", new Vector2(1000, 960), Vector2.zero);
            Label(box, "Title", "SETTINGS", 64, FontStyle.Bold, Color.white, new Vector2(0, 415), new Vector2(900, 90));

            float y = 330;
            Section(box, "AUDIO", ref y);
            Slider masterVolume = SliderRow(box, "Master volume", ref y);
            Slider musicVolume = SliderRow(box, "Music", ref y);
            Slider effectsVolume = SliderRow(box, "Sound effects", ref y);
            Slider ambienceVolume = SliderRow(box, "Ambience", ref y);
            Section(box, "CONTROLS", ref y);
            Slider lookSensitivity = SliderRow(box, "Look sensitivity", ref y);
            lookSensitivity.minValue = Core.GameSettings.MinLookSensitivity;
            lookSensitivity.maxValue = Core.GameSettings.MaxLookSensitivity;
            lookSensitivity.value = Core.GameSettings.DefaultLookSensitivity;
            Toggle invertY = ToggleRow(box, "Invert look up / down", ref y);
            Section(box, "DISPLAY", ref y);
            Toggle fullscreen = ToggleRow(box, "Fullscreen", ref y);
            Toggle vSync = ToggleRow(box, "V-Sync (no tearing)", ref y);
            QualityRow(box, ref y, out Button qualityDown, out Button qualityUp, out Text qualityLabel);

            Button reset = MakeButton(box, "ResetButton", "RESET DEFAULTS", new Vector2(-170, -415), new Vector2(300, 64),
                false);
            Button back = MakeButton(box, "BackButton", "BACK", new Vector2(170, -415), new Vector2(300, 64), true);

            var panel = root.AddComponent<SettingsPanelUI>();
            SetFields(panel, ("masterVolume", masterVolume), ("musicVolume", musicVolume), ("effectsVolume", effectsVolume),
                ("ambienceVolume", ambienceVolume), ("lookSensitivity", lookSensitivity), ("invertY", invertY),
                ("fullscreen", fullscreen), ("vSync", vSync), ("qualityDown", qualityDown), ("qualityUp", qualityUp),
                ("qualityLabel", qualityLabel), ("resetButton", reset), ("backButton", back));

            PrefabUtility.SaveAsPrefabAsset(root, SettingsPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void Section(RectTransform box, string title, ref float y)
        {
            Text text = Label(box, title, title, 24, FontStyle.Bold, Accent, new Vector2(-40, y), new Vector2(820, 40));
            text.alignment = TextAnchor.MiddleLeft;
            y -= 50;
        }

        private static RectTransform Row(RectTransform box, string label, ref float y)
        {
            RectTransform row = Rect(label, box, new Vector2(0, y), new Vector2(860, 56));
            Text text = Label(row, "Label", label, 28, FontStyle.Normal, TextLight, new Vector2(-235, 0), new Vector2(390, 56));
            text.alignment = TextAnchor.MiddleLeft;
            y -= 62;
            return row;
        }

        private static Slider SliderRow(RectTransform box, string label, ref float y)
        {
            RectTransform row = Row(box, label, ref y);
            Text value = Label(row, "Value", "100%", 26, FontStyle.Bold, Color.white, new Vector2(375, 0), new Vector2(110, 56));
            value.alignment = TextAnchor.MiddleRight;

            RectTransform sliderRect = Rect("Slider", row, new Vector2(140, 0), new Vector2(380, 36));
            var slider = sliderRect.gameObject.AddComponent<Slider>();
            RectTransform track = Rect("Background", sliderRect, Vector2.zero, Vector2.zero);
            Stretch(track, new Vector4(0, 10, 0, 10));
            AddImage(track.gameObject, TrackColor, rounded);
            RectTransform fillArea = Rect("Fill Area", sliderRect, Vector2.zero, Vector2.zero);
            Stretch(fillArea, new Vector4(0, 10, 0, 10));
            RectTransform fill = Rect("Fill", fillArea, Vector2.zero, Vector2.zero);
            Stretch(fill);
            AddImage(fill.gameObject, Accent, rounded);
            RectTransform handleArea = Rect("Handle Slide Area", sliderRect, Vector2.zero, Vector2.zero);
            Stretch(handleArea, new Vector4(14, 0, 14, 0));
            RectTransform handle = Rect("Handle", handleArea, Vector2.zero, new Vector2(34, 34));
            handle.anchorMin = handle.anchorMax = new Vector2(0, 0.5f);
            Image knob = AddImage(handle.gameObject, Color.white, circle);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight;
            slider.colors = Tint(Color.white, AccentBright, new Color(0.9f, 0.9f, 0.9f));
            Hover(slider);
            return slider;
        }

        private static Toggle ToggleRow(RectTransform box, string label, ref float y)
        {
            RectTransform row = Row(box, label, ref y);
            RectTransform toggleRect = Rect("Toggle", row, new Vector2(-30, 0), new Vector2(44, 44));
            var toggle = toggleRect.gameObject.AddComponent<Toggle>();
            Image background = AddImage(toggleRect.gameObject, Color.white, rounded);
            RectTransform check = Rect("Checkmark", toggleRect, Vector2.zero, new Vector2(26, 26));
            AddImage(check.gameObject, Accent, rounded);
            toggle.targetGraphic = background;
            toggle.graphic = check.GetComponent<Image>();
            toggle.colors = Tint(TrackColor, ButtonGrey, TrackColor);
            Hover(toggle);
            return toggle;
        }

        private static void QualityRow(RectTransform box, ref float y, out Button down, out Button up, out Text label)
        {
            RectTransform row = Row(box, "Graphics quality", ref y);
            down = MakeButton(row, "QualityDown", "<", new Vector2(-30, 0), new Vector2(56, 48), false);
            up = MakeButton(row, "QualityUp", ">", new Vector2(310, 0), new Vector2(56, 48), false);
            label = Label(row, "Quality", "HIGH", 26, FontStyle.Bold, Color.white, new Vector2(140, 0), new Vector2(250, 48));
        }

        // =====================================================================
        // Menu scene
        // =====================================================================

        private static void BuildMenuScene()
        {
            // Build in a fresh scene, then put back the saved scenes that were open (the menu itself is rebuilt, and
            // an untitled scene cannot sit beside a new one).
            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup()
                .Where(s => !string.IsNullOrEmpty(s.path) && s.path != MenuScenePath).ToArray();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            LoadSprites();
            SettingsPanelUI settingsPrefab = LoadSettingsPrefab();

            var cameraObject = new GameObject("MenuCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.16f);
            camera.cullingMask = 0;

            // No EventSystem here: MainMenuUI makes the shared one (UiEventSystem), which lives on across scenes, so
            // returning from the game never leaves two.

            var canvasObject = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            RectTransform root = canvasObject.GetComponent<RectTransform>();

            BuildBackdrop(root);

            // Main screen.
            RectTransform main = Rect("MainPanel", root, Vector2.zero, Vector2.zero);
            Stretch(main);
            string[] names = { "PLAY", "HOW TO PLAY", "SETTINGS", "CREDITS", "QUIT" };
            var buttons = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
                buttons[i] = MakeButton(main, names[i].Replace(" ", "") + "Button", names[i], new Vector2(0, 70 - i * 84),
                    new Vector2(440, 72), i == 0, 32);
            Text hint = Label(main, "Hint", "Mouse, arrow keys or gamepad to choose  ·  Enter / A to select  ·  Esc / B to go back",
                22, FontStyle.Normal, TextDim, new Vector2(0, -505), new Vector2(1600, 40));
            hint.alignment = TextAnchor.MiddleCenter;
            Text version = Label(main, "Version", $"v{Application.version}", 20, FontStyle.Normal, TextDim, Vector2.zero, new Vector2(200, 40));
            Anchor(version.rectTransform, new Vector2(1, 0), new Vector2(-30, 25), TextAnchor.LowerRight);

            // How to play.
            RectTransform howTo = Dialog(root, "HowToPlayPanel", new Vector2(1500, 860), "HOW TO PLAY", out RectTransform howBox);
            BuildHowToPlay(howBox);
            Button howBack = MakeButton(howBox, "BackButton", "BACK", new Vector2(0, -375), new Vector2(300, 64), true);

            // Credits.
            RectTransform credits = Dialog(root, "CreditsPanel", new Vector2(1000, 760), "CREDITS", out RectTransform creditsBox);
            Text creditsText = Label(creditsBox, "Body",
                "<b>RUNWAY PANIC</b>\nAirport Bird Control\n\n" +
                "<color=#E0731F>SE3032 Graphics & Visualization  ×  SE3062 Intelligent Systems</color>\nSLIIT group project\n\n" +
                "<b>World Builder</b>  ·  airport, level and integration\n" +
                "<b>Systems Engineer</b>  ·  player, weapons, shop, pickups, menus, audio\n" +
                "<b>Core Developer</b>  ·  3D models and bird prefabs\n" +
                "<b>Agent Controller</b>  ·  bird movement and animation\n" +
                "<b>Intelligent Systems</b>  ·  A* flight pathfinding and the gull, pigeon, crow and hawk brains\n\n" +
                "Made with Unity 6 · all sound generated in-project",
                26, FontStyle.Normal, TextLight, new Vector2(0, 20), new Vector2(900, 560));
            creditsText.supportRichText = true;
            creditsText.lineSpacing = 1.15f;
            Button creditsBack = MakeButton(creditsBox, "BackButton", "BACK", new Vector2(0, -320), new Vector2(300, 64), true);

            // Quit confirmation.
            RectTransform quit = Dialog(root, "QuitPanel", new Vector2(760, 340), "QUIT THE GAME?", out RectTransform quitBox);
            Label(quitBox, "Detail", "The birds will still be here when you come back.", 26, FontStyle.Normal, TextLight,
                new Vector2(0, 20), new Vector2(680, 60));
            Button quitNo = MakeButton(quitBox, "NoButton", "STAY", new Vector2(-150, -100), new Vector2(260, 64), true);
            Button quitYes = MakeButton(quitBox, "YesButton", "QUIT", new Vector2(150, -100), new Vector2(260, 64), false);

            // Settings (shared prefab).
            var settingsObject = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab.gameObject, root);
            Stretch(settingsObject.GetComponent<RectTransform>());
            settingsObject.SetActive(false);

            // Loading.
            RectTransform loading = Rect("LoadingPanel", root, Vector2.zero, Vector2.zero);
            Stretch(loading);
            AddImage(loading.gameObject, new Color(0.03f, 0.04f, 0.06f, 0.97f), null);
            Label(loading, "Title", "PREPARING THE AIRPORT...", 52, FontStyle.Bold, Color.white, new Vector2(0, 110), new Vector2(1400, 80));
            RectTransform bar = Rect("Bar", loading, new Vector2(0, 0), new Vector2(900, 26));
            AddImage(bar.gameObject, TrackColor, rounded);
            RectTransform fill = Rect("Fill", bar, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            AddImage(fill.gameObject, Accent, rounded);
            Text percent = Label(loading, "Percent", "0%", 28, FontStyle.Bold, TextLight, new Vector2(0, -50), new Vector2(300, 40));
            Text tip = Label(loading, "Tip", "", 28, FontStyle.Italic, TextDim, new Vector2(0, -170), new Vector2(1300, 120));

            // Hover-selects for everything in the menu.
            foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
                if (selectable.GetComponent<SelectOnHover>() == null)
                    selectable.gameObject.AddComponent<SelectOnHover>();

            var menu = canvasObject.AddComponent<MainMenuUI>();
            SetFields(menu, ("mainPanel", main.gameObject), ("playButton", buttons[0]), ("howToPlayButton", buttons[1]),
                ("settingsButton", buttons[2]), ("creditsButton", buttons[3]), ("quitButton", buttons[4]),
                ("howToPlayPanel", howTo.gameObject), ("howToPlayBack", howBack), ("creditsPanel", credits.gameObject),
                ("creditsBack", creditsBack), ("settingsPanel", settingsObject.GetComponent<SettingsPanelUI>()),
                ("quitPanel", quit.gameObject), ("quitYes", quitYes), ("quitNo", quitNo),
                ("loadingPanel", loading.gameObject), ("loadingFill", fill), ("loadingPercent", percent), ("loadingTip", tip),
                ("music", Cue("SC_Music_Ambient")), ("ambience", Cue("SC_Ambience_Airport")), ("click", Cue("SC_UI_Click")));

            howTo.gameObject.SetActive(false);
            credits.gameObject.SetActive(false);
            quit.gameObject.SetActive(false);
            loading.gameObject.SetActive(false);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            if (previous.Length > 0)
            {
                if (!previous.Any(s => s.isActive))
                    previous[0].isActive = true;
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }

        private static void BuildBackdrop(RectTransform root)
        {
            RectTransform backdrop = Rect("Backdrop", root, Vector2.zero, Vector2.zero);
            Stretch(backdrop);
            AddImage(backdrop.gameObject, Color.white, sky).raycastTarget = false;

            // Low sun glow at the horizon, then the ground and the runway with its edge lights.
            RectTransform sun = Rect("Sun", backdrop, new Vector2(560, -250), new Vector2(560, 560));
            AddImage(sun.gameObject, new Color(1f, 0.72f, 0.36f, 0.35f), circle).raycastTarget = false;
            RectTransform ground = Rect("Ground", backdrop, Vector2.zero, Vector2.zero);
            ground.anchorMin = Vector2.zero;
            ground.anchorMax = new Vector2(1f, 0.22f);
            ground.offsetMin = ground.offsetMax = Vector2.zero;
            AddImage(ground.gameObject, new Color(0.05f, 0.06f, 0.07f, 1f), null).raycastTarget = false;
            RectTransform runway = Rect("Runway", backdrop, new Vector2(0, -415), new Vector2(4000, 90));
            AddImage(runway.gameObject, new Color(0.12f, 0.13f, 0.15f, 1f), null).raycastTarget = false;
            for (int i = 0; i < 24; i++)
            {
                RectTransform dash = Rect("Dash", runway, new Vector2(-1150 + i * 100, 0), new Vector2(50, 6));
                AddImage(dash.gameObject, new Color(0.85f, 0.85f, 0.8f, 0.55f), null).raycastTarget = false;
            }

            var lights = new List<Image>();
            for (int i = 0; i < 26; i++)
                foreach (float edge in new[] { 52f, -52f })
                {
                    RectTransform lamp = Rect("Light", runway, new Vector2(-1250 + i * 100, edge), new Vector2(12, 12));
                    Image image = AddImage(lamp.gameObject, i % 2 == 0 ? new Color(1f, 0.75f, 0.3f) : new Color(0.55f, 0.85f, 1f), circle);
                    image.raycastTarget = false;
                    if (edge > 0f) lights.Add(image);
                    else lights.Insert(lights.Count / 2, image);
                }

            var birds = new List<RectTransform>();
            var random = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                float size = 0.25f + (float)random.NextDouble() * 0.45f;
                RectTransform b = Rect("Bird", backdrop, new Vector2(-900 + (float)random.NextDouble() * 1800f, -40 + (float)random.NextDouble() * 480f),
                    new Vector2(128, 64));
                b.localScale = new Vector3(size, size, 1f);
                AddImage(b.gameObject, new Color(0.04f, 0.04f, 0.08f, 0.55f + size * 0.4f), bird).raycastTarget = false;
                birds.Add(b);
            }

            // Title block.
            RectTransform title = Rect("Title", root, new Vector2(0, 330), new Vector2(1400, 260));
            Text titleText = Label(title, "Name", "RUNWAY PANIC", 128, FontStyle.Bold, Color.white, new Vector2(0, 30), new Vector2(1400, 160));
            var outline = titleText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.2f, 0.05f, 0.9f);
            outline.effectDistance = new Vector2(3, -3);
            var shadow = titleText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(6, -8);
            Label(title, "Subtitle", "A I R P O R T   B I R D   C O N T R O L", 30, FontStyle.Bold, AccentBright, new Vector2(0, -75), new Vector2(1400, 50));

            var animator = backdrop.gameObject.AddComponent<MenuBackdrop>();
            SetFields(animator, ("birds", birds.ToArray()), ("runwayLights", lights.ToArray()), ("title", title));
        }

        private static void BuildHowToPlay(RectTransform box)
        {
            Text goal = Label(box, "Goal",
                "<color=#F09A38><b>THE GOAL</b></color>\n" +
                "Birds are taking over the runway. Survive <b>4 waves</b> and clear the airport.\n\n" +
                "<color=#F09A38><b>THE RIGHT GUN</b></color>\n" +
                "Every bird needs its own gun. The wrong gun does only <b>10%</b> damage.\n" +
                "  Gull  —  Pistol\n  Pigeon  —  Assault Rifle\n  Crow  —  Sniper\n  Hawk  —  RPG\n\n" +
                "<color=#F09A38><b>BETWEEN WAVES</b></color>\n" +
                "Spend coins from kills in the shop: new guns, medkits and shields.\n\n" +
                "<color=#F09A38><b>SURVIVE</b></color>\n" +
                "You have 3 lives. Hide in shelters, push barricades for cover and watch the crows: they steal pickups.",
                25, FontStyle.Normal, TextLight, new Vector2(-355, -25), new Vector2(680, 620));
            goal.alignment = TextAnchor.UpperLeft;
            goal.supportRichText = true;
            goal.lineSpacing = 1.1f;

            (string action, string keys, string pad)[] controls =
            {
                ("Move", "W A S D", "Left stick"),
                ("Look / aim", "Mouse", "Right stick"),
                ("Sprint", "Shift", "L3"),
                ("Jump", "Space", "A"),
                ("Shoot", "Left mouse", "X"),
                ("Reload", "R", "RB"),
                ("Switch gun", "1 / 2", "D-pad left / right"),
                ("Interact (doors)", "E", "Y"),
                ("Use medkit", "Q", "D-pad up"),
                ("Use shield", "F", "D-pad down"),
                ("Pause", "Esc", "Start"),
            };

            RectTransform table = Rect("Controls", box, new Vector2(380, -25), new Vector2(660, 640));
            Text header = Label(table, "Header", "<color=#F09A38><b>CONTROLS</b></color>", 25, FontStyle.Normal, TextLight,
                new Vector2(0, 300), new Vector2(660, 40));
            header.supportRichText = true;
            header.alignment = TextAnchor.MiddleLeft;
            for (int i = 0; i < controls.Length; i++)
            {
                float y = 250 - i * 50;
                if (i % 2 == 0)
                {
                    RectTransform stripe = Rect("Stripe", table, new Vector2(0, y), new Vector2(660, 46));
                    AddImage(stripe.gameObject, new Color(1f, 1f, 1f, 0.04f), rounded).raycastTarget = false;
                }

                Text action = Label(table, "Action", controls[i].action, 24, FontStyle.Normal, TextLight, new Vector2(-195, y), new Vector2(250, 46));
                action.alignment = TextAnchor.MiddleLeft;
                Text keys = Label(table, "Keys", controls[i].keys, 24, FontStyle.Bold, Color.white, new Vector2(60, y), new Vector2(200, 46));
                keys.alignment = TextAnchor.MiddleLeft;
                Text pad = Label(table, "Pad", controls[i].pad, 22, FontStyle.Normal, TextDim, new Vector2(250, y), new Vector2(200, 46));
                pad.alignment = TextAnchor.MiddleLeft;
            }
        }

        // A full-screen dimmed screen with a titled box.
        private static RectTransform Dialog(RectTransform root, string name, Vector2 size, string title, out RectTransform box)
        {
            RectTransform screen = Rect(name, root, Vector2.zero, Vector2.zero);
            Stretch(screen);
            AddImage(screen.gameObject, DimColor, null);
            box = Panel(screen, "Box", size, Vector2.zero);
            Label(box, "Title", title, 56, FontStyle.Bold, Color.white, new Vector2(0, size.y * 0.5f - 70), new Vector2(size.x - 60, 80));
            return screen;
        }

        // =====================================================================
        // In-game screens on P_GameManager
        // =====================================================================

        private static void UpdateGameScreens()
        {
            LoadSprites();
            SettingsPanelUI settingsPrefab = LoadSettingsPrefab();
            GameObject root = PrefabUtility.LoadPrefabContents(GameManagerPrefab);
            try
            {
                Transform canvas = root.transform.Find("MenuCanvas");
                var screens = root.GetComponentInChildren<MenuScreensUI>(true);

                // Pause: a column of five buttons.
                RectTransform pauseBox = (RectTransform)canvas.Find("PauseScreen/Box");
                pauseBox.sizeDelta = new Vector2(620, 640);
                Place(pauseBox, "Title", new Vector2(0, 245), new Vector2(560, 100));
                Place(pauseBox, "Detail", new Vector2(0, 172), new Vector2(560, 50));
                pauseBox.Find("Detail").GetComponent<Text>().text = "Press Esc to resume.";
                Button resume = StyleButton(pauseBox.Find("ResumeButton"), true, new Vector2(0, 86));
                Button restart = StyleButton(pauseBox.Find("RestartButton"), false, new Vector2(0, 4));
                Button settings = EnsureButton(pauseBox, "SettingsButton", "SETTINGS", restart, new Vector2(0, -78));
                Button pauseMenu = EnsureButton(pauseBox, "MainMenuButton", "MAIN MENU", restart, new Vector2(0, -160));
                Button pauseQuit = StyleButton(pauseBox.Find("QuitButton"), false, new Vector2(0, -242));
                foreach (Button b in new[] { resume, restart, settings, pauseMenu, pauseQuit })
                    ((RectTransform)b.transform).sizeDelta = new Vector2(380, 64);

                // Game over and victory: Restart · Main Menu · Quit.
                var menuButtons = new List<Button> { pauseMenu };
                foreach (string name in new[] { "GameOverScreen", "VictoryScreen" })
                {
                    RectTransform box = (RectTransform)canvas.Find($"{name}/Box");
                    Button again = StyleButton(box.Find("RestartButton"), true, new Vector2(-250, -140));
                    Button toMenu = EnsureButton(box, "MainMenuButton", "MAIN MENU", again, new Vector2(0, -140));
                    StyleButton(toMenu.transform, false, new Vector2(0, -140));
                    StyleButton(box.Find("QuitButton"), false, new Vector2(250, -140));
                    menuButtons.Add(toMenu);
                }

                // Rounded boxes, like the menu.
                foreach (string name in new[] { "PauseScreen", "GameOverScreen", "VictoryScreen" })
                {
                    var boxImage = canvas.Find($"{name}/Box").GetComponent<Image>();
                    boxImage.sprite = rounded;
                    boxImage.type = Image.Type.Sliced;
                }

                // The shared settings panel, on top of everything.
                Transform existing = canvas.Find("P_SettingsPanel");
                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);
                var settingsObject = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab.gameObject, canvas);
                settingsObject.name = "P_SettingsPanel";
                Stretch(settingsObject.GetComponent<RectTransform>());
                settingsObject.transform.SetAsLastSibling();
                settingsObject.SetActive(false);

                foreach (Selectable selectable in canvas.GetComponentsInChildren<Selectable>(true))
                    if (selectable.GetComponent<SelectOnHover>() == null)
                        selectable.gameObject.AddComponent<SelectOnHover>();

                SetFields(screens, ("mainMenuButtons", menuButtons.ToArray()), ("settingsButton", settings),
                    ("settingsPanel", settingsObject.GetComponent<SettingsPanelUI>()));
                PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Place(Transform parent, string child, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)parent.Find(child);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static Button EnsureButton(Transform parent, string name, string label, Button template, Vector2 position)
        {
            Transform existing = parent.Find(name);
            GameObject copy = existing != null ? existing.gameObject : Object.Instantiate(template.gameObject, parent);
            copy.name = name;
            var button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent(); // the template's persistent listeners must not carry over
            copy.GetComponentInChildren<Text>().text = label;
            return StyleButton(copy.transform, false, position);
        }

        private static Button StyleButton(Transform target, bool primary, Vector2 position)
        {
            var button = target.GetComponent<Button>();
            ((RectTransform)target).anchoredPosition = position;
            var image = target.GetComponent<Image>();
            image.color = Color.white;
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
            button.colors = ButtonColors(primary);
            return button;
        }

        // =====================================================================
        // Build scenes
        // =====================================================================

        private static void UpdateBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuScenePath, true) };
            bool hasGame = false;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path == MenuScenePath)
                    continue;
                hasGame |= scene.path == GameScenePath;
                scenes.Add(scene);
            }

            if (!hasGame)
                scenes.Insert(1, new EditorBuildSettingsScene(GameScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // =====================================================================
        // Small UI helpers
        // =====================================================================

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector4 inset = default)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset.x, inset.y);
            rect.offsetMax = new Vector2(-inset.z, -inset.w);
        }

        private static void Anchor(RectTransform rect, Vector2 corner, Vector2 offset, TextAnchor alignment)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = offset;
            if (rect.TryGetComponent(out Text text))
                text.alignment = alignment;
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 size, Vector2 position)
        {
            RectTransform panel = Rect(name, parent, position, size);
            AddImage(panel.gameObject, PanelColor, rounded);
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            outline.effectDistance = new Vector2(2, -2);
            return panel;
        }

        private static Image AddImage(GameObject target, Color color, Sprite sprite)
        {
            var image = target.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            if (sprite != null && sprite.border != Vector4.zero)
                image.type = Image.Type.Sliced;
            return image;
        }

        private static Text Label(Transform parent, string name, string text, int size, FontStyle style, Color color,
            Vector2 position, Vector2 box)
        {
            RectTransform rect = Rect(name, parent, position, box);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        private static Button MakeButton(Transform parent, string name, string text, Vector2 position, Vector2 size,
            bool primary, int fontSize = 26)
        {
            RectTransform rect = Rect(name, parent, position, size);
            Image image = AddImage(rect.gameObject, Color.white, rounded);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = ButtonColors(primary);
            Text label = Label(rect, "Label", text, fontSize, FontStyle.Bold, Color.white, Vector2.zero, size);
            Stretch(label.rectTransform);
            return button;
        }

        // Primary buttons are orange; the rest grey, turning orange when highlighted or selected.
        private static ColorBlock ButtonColors(bool primary) => new ColorBlock
        {
            normalColor = primary ? Accent : ButtonGrey,
            highlightedColor = AccentBright,
            selectedColor = AccentBright,
            pressedColor = AccentDark,
            disabledColor = new Color(ButtonGrey.r, ButtonGrey.g, ButtonGrey.b, 0.4f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };

        private static ColorBlock Tint(Color normal, Color highlighted, Color pressed) => new ColorBlock
        {
            normalColor = normal,
            highlightedColor = highlighted,
            selectedColor = highlighted,
            pressedColor = pressed,
            disabledColor = new Color(normal.r, normal.g, normal.b, 0.4f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };

        private static void Hover(Selectable selectable) => selectable.gameObject.AddComponent<SelectOnHover>();

        private static SoundCue Cue(string name) => AssetDatabase.LoadAssetAtPath<SoundCue>($"{CueFolder}/{name}.asset");

        private static void SetFields(Object target, params (string field, object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach ((string field, object value) in fields)
            {
                SerializedProperty property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogWarning($"[MainMenu] {target.GetType().Name} has no field '{field}'.");
                    continue;
                }

                if (value is System.Array array)
                {
                    property.arraySize = array.Length;
                    for (int i = 0; i < array.Length; i++)
                        property.GetArrayElementAtIndex(i).objectReferenceValue = (Object)array.GetValue(i);
                }
                else
                {
                    property.objectReferenceValue = (Object)value;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
