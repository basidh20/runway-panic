using System;
using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// The settings screen, shared by the main menu and the pause screen (<c>Prefabs/UI/P_SettingsPanel</c>):
    /// volume sliders, look sensitivity, invert Y, fullscreen, V-Sync and graphics quality (shown only when the project
    /// has more than one level). Every change applies at once through <see cref="GameSettings"/> and is saved when the
    /// panel closes. Back (or Esc) closes it.
    /// </summary>
    public sealed class SettingsPanelUI : MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] private Slider masterVolume;
        [SerializeField] private Slider musicVolume;
        [SerializeField] private Slider effectsVolume;
        [SerializeField] private Slider ambienceVolume;

        [Header("Controls")]
        [SerializeField] private Slider lookSensitivity;
        [SerializeField] private Toggle invertY;

        [Header("Display")]
        [SerializeField] private Toggle fullscreen;
        [SerializeField] private Toggle vSync;
        [SerializeField] private Button qualityDown;
        [SerializeField] private Button qualityUp;
        [SerializeField] private Text qualityLabel;

        [Header("Buttons")]
        [SerializeField] private Button resetButton;
        [SerializeField] private Button backButton;

        private bool refreshing;

        /// <summary>Raised when the panel closes (Back or Esc), so the screen that opened it can take focus back.</summary>
        public event Action Closed;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            Bind(masterVolume, v => GameSettings.MasterVolume = v);
            Bind(musicVolume, v => GameSettings.MusicVolume = v);
            Bind(effectsVolume, v => GameSettings.EffectsVolume = v);
            Bind(ambienceVolume, v => GameSettings.AmbienceVolume = v);
            // Range first: changing it clamps the value, which must not reach the setting.
            if (lookSensitivity != null)
            {
                lookSensitivity.minValue = GameSettings.MinLookSensitivity;
                lookSensitivity.maxValue = GameSettings.MaxLookSensitivity;
            }
            Bind(lookSensitivity, v => GameSettings.LookSensitivity = v);

            if (invertY != null)
                invertY.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.InvertY = v; });
            if (fullscreen != null)
                fullscreen.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.Fullscreen = v; });
            if (vSync != null)
                vSync.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.VSync = v; });
            if (qualityDown != null)
                qualityDown.onClick.AddListener(() => GameSettings.Quality--);
            if (qualityUp != null)
                qualityUp.onClick.AddListener(() => GameSettings.Quality++);
            if (resetButton != null)
                resetButton.onClick.AddListener(GameSettings.RestoreDefaults);
            if (backButton != null)
                backButton.onClick.AddListener(Close);

            // A project with a single quality level has nothing to choose: hide the row.
            if (qualityLabel != null)
                qualityLabel.transform.parent.gameObject.SetActive(QualitySettings.names.Length > 1);
        }

        private void OnEnable()
        {
            GameSettings.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            GameSettings.Changed -= Refresh;
            GameSettings.Save();
        }

        /// <summary>Shows the panel and puts keyboard / gamepad focus on its first slider.</summary>
        public void Open()
        {
            gameObject.SetActive(true);
            Select(masterVolume != null ? masterVolume.gameObject : backButton != null ? backButton.gameObject : null);
        }

        /// <summary>Saves, hides the panel and tells the screen that opened it.</summary>
        public void Close()
        {
            if (!IsOpen)
                return;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void Bind(Slider slider, Action<float> set)
        {
            if (slider == null)
                return;
            slider.onValueChanged.AddListener(v =>
            {
                if (!refreshing)
                    set(v);
                ShowValue(slider);
            });
        }

        private void Refresh()
        {
            refreshing = true;
            SetSlider(masterVolume, GameSettings.MasterVolume);
            SetSlider(musicVolume, GameSettings.MusicVolume);
            SetSlider(effectsVolume, GameSettings.EffectsVolume);
            SetSlider(ambienceVolume, GameSettings.AmbienceVolume);
            SetSlider(lookSensitivity, GameSettings.LookSensitivity);
            if (invertY != null) invertY.isOn = GameSettings.InvertY;
            if (fullscreen != null) fullscreen.isOn = GameSettings.Fullscreen;
            if (vSync != null) vSync.isOn = GameSettings.VSync;
            if (qualityLabel != null) qualityLabel.text = QualityName(GameSettings.Quality);
            if (qualityDown != null) qualityDown.interactable = GameSettings.Quality > 0;
            if (qualityUp != null) qualityUp.interactable = GameSettings.Quality < QualitySettings.names.Length - 1;
            refreshing = false;
        }

        // The project's levels are named for platforms ("Mobile", "PC"); players read them as low / high.
        private static string QualityName(int level)
        {
            int count = QualitySettings.names.Length;
            if (count == 2)
                return level == 0 ? "LOW" : "HIGH";
            if (count == 3)
                return new[] { "LOW", "MEDIUM", "HIGH" }[level];
            return QualitySettings.names[level].ToUpperInvariant();
        }

        private static void SetSlider(Slider slider, float value)
        {
            if (slider == null)
                return;
            slider.SetValueWithoutNotify(value);
            ShowValue(slider);
        }

        // Each slider has a "Value" text child: a percentage for volumes, "1.0×" for sensitivity.
        private static void ShowValue(Slider slider)
        {
            Transform label = slider.transform.parent != null ? slider.transform.parent.Find("Value") : null;
            if (label == null || !label.TryGetComponent(out Text text))
                return;
            text.text = slider.maxValue > 1.01f ? $"{slider.value:0.0}×" : $"{Mathf.RoundToInt(slider.value * 100f)}%";
        }

        private static void Select(GameObject target)
        {
            if (EventSystem.current != null && target != null)
                EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
