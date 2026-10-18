using System;
using System.Collections.Generic;
using RunwayPanic.Audio;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Core
{
    /// <summary>
    /// The player's settings — volumes, look sensitivity, invert Y, fullscreen, V-Sync and graphics quality — saved
    /// with <see cref="PlayerPrefs"/> and applied at start-up and to every scene that loads. Change a value through its
    /// property, then call <see cref="Save"/> (the settings screen does both); <see cref="Changed"/> fires on every
    /// change, and the new value is applied at once.
    /// </summary>
    public static class GameSettings
    {
        public const float DefaultMasterVolume = 0.8f;
        public const float DefaultMusicVolume = 0.7f;
        public const float DefaultEffectsVolume = 1f;
        public const float DefaultAmbienceVolume = 0.8f;
        public const float DefaultLookSensitivity = 1f;
        public const float MinLookSensitivity = 0.2f;
        public const float MaxLookSensitivity = 3f;

        private const string Prefix = "RunwayPanic.Settings.";

        private static float masterVolume = DefaultMasterVolume;
        private static float musicVolume = DefaultMusicVolume;
        private static float effectsVolume = DefaultEffectsVolume;
        private static float ambienceVolume = DefaultAmbienceVolume;
        private static float lookSensitivity = DefaultLookSensitivity;
        private static bool invertY;
        private static bool fullscreen = true;
        private static bool vSync = true;
        private static int quality = -1; // -1 = the project's default level

        // Each camera's own gains, so sensitivity always scales the designed values rather than compounding.
        private static readonly Dictionary<CinemachineInputAxisController, float[]> baseGains =
            new Dictionary<CinemachineInputAxisController, float[]>();

        /// <summary>Raised after any setting changes (or the defaults are restored).</summary>
        public static event Action Changed;

        public static float MasterVolume
        {
            get => masterVolume;
            set => Set(ref masterVolume, Mathf.Clamp01(value), ApplyAudio);
        }

        public static float MusicVolume
        {
            get => musicVolume;
            set => Set(ref musicVolume, Mathf.Clamp01(value), ApplyAudio);
        }

        /// <summary>Guns, the player, birds, the world and the interface.</summary>
        public static float EffectsVolume
        {
            get => effectsVolume;
            set => Set(ref effectsVolume, Mathf.Clamp01(value), ApplyAudio);
        }

        public static float AmbienceVolume
        {
            get => ambienceVolume;
            set => Set(ref ambienceVolume, Mathf.Clamp01(value), ApplyAudio);
        }

        /// <summary>Camera look speed as a multiple of the designed speed.</summary>
        public static float LookSensitivity
        {
            get => lookSensitivity;
            set => Set(ref lookSensitivity, Mathf.Clamp(value, MinLookSensitivity, MaxLookSensitivity), ApplyLook);
        }

        public static bool InvertY
        {
            get => invertY;
            set => Set(ref invertY, value, ApplyLook);
        }

        public static bool Fullscreen
        {
            get => fullscreen;
            set => Set(ref fullscreen, value, ApplyDisplay);
        }

        /// <summary>Waits for the monitor's refresh: no tearing, frame rate capped at the refresh rate.</summary>
        public static bool VSync
        {
            get => vSync;
            set => Set(ref vSync, value, ApplyDisplay);
        }

        /// <summary>Index into <see cref="QualitySettings.names"/>.</summary>
        public static int Quality
        {
            get => quality < 0 ? QualitySettings.GetQualityLevel() : quality;
            set => Set(ref quality, Mathf.Clamp(value, 0, QualitySettings.names.Length - 1), ApplyDisplay);
        }

        /// <summary>Writes every setting to disk.</summary>
        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "MasterVolume", masterVolume);
            PlayerPrefs.SetFloat(Prefix + "MusicVolume", musicVolume);
            PlayerPrefs.SetFloat(Prefix + "EffectsVolume", effectsVolume);
            PlayerPrefs.SetFloat(Prefix + "AmbienceVolume", ambienceVolume);
            PlayerPrefs.SetFloat(Prefix + "LookSensitivity", lookSensitivity);
            PlayerPrefs.SetInt(Prefix + "InvertY", invertY ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Fullscreen", fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "VSync", vSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Quality", quality);
            PlayerPrefs.Save();
        }

        /// <summary>Puts every setting back to its default and applies it (call <see cref="Save"/> to keep it).</summary>
        public static void RestoreDefaults()
        {
            masterVolume = DefaultMasterVolume;
            musicVolume = DefaultMusicVolume;
            effectsVolume = DefaultEffectsVolume;
            ambienceVolume = DefaultAmbienceVolume;
            lookSensitivity = DefaultLookSensitivity;
            invertY = false;
            fullscreen = true;
            vSync = true;
            quality = -1;
            ApplyAll();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialise()
        {
            baseGains.Clear();
            Changed = null;
            Load();
            ApplyAudio();
            ApplyDisplay();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void Load()
        {
            masterVolume = PlayerPrefs.GetFloat(Prefix + "MasterVolume", DefaultMasterVolume);
            musicVolume = PlayerPrefs.GetFloat(Prefix + "MusicVolume", DefaultMusicVolume);
            effectsVolume = PlayerPrefs.GetFloat(Prefix + "EffectsVolume", DefaultEffectsVolume);
            ambienceVolume = PlayerPrefs.GetFloat(Prefix + "AmbienceVolume", DefaultAmbienceVolume);
            lookSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "LookSensitivity", DefaultLookSensitivity),
                MinLookSensitivity, MaxLookSensitivity);
            invertY = PlayerPrefs.GetInt(Prefix + "InvertY", 0) == 1;
            fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", 1) == 1;
            vSync = PlayerPrefs.GetInt(Prefix + "VSync", 1) == 1;
            quality = PlayerPrefs.GetInt(Prefix + "Quality", -1);
            if (quality >= QualitySettings.names.Length)
                quality = -1;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var gone = new List<CinemachineInputAxisController>();
            foreach (CinemachineInputAxisController input in baseGains.Keys)
                if (input == null)
                    gone.Add(input);
            foreach (CinemachineInputAxisController input in gone)
                baseGains.Remove(input);

            ApplyLook();
        }

        private static void Set<T>(ref T field, T value, Action apply)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            apply();
            Changed?.Invoke();
        }

        private static void ApplyAll()
        {
            ApplyAudio();
            ApplyLook();
            ApplyDisplay();
        }

        private static void ApplyAudio()
        {
            SoundPlayer.SetMasterVolume(masterVolume);
            SoundPlayer.SetVolume(SoundCategory.Music, musicVolume);
            SoundPlayer.SetVolume(SoundCategory.Ambience, ambienceVolume);
            foreach (SoundCategory category in new[]
                         { SoundCategory.Weapons, SoundCategory.Player, SoundCategory.Birds, SoundCategory.World, SoundCategory.UI })
                SoundPlayer.SetVolume(category, effectsVolume);
        }

        private static void ApplyDisplay()
        {
            QualitySettings.vSyncCount = vSync ? 1 : 0;
            if (quality >= 0 && quality != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(quality, true);

            // Only in a player build: in the Editor, fullscreen would take over the Game view.
            if (!Application.isEditor && Screen.fullScreen != fullscreen)
                Screen.fullScreen = fullscreen;
        }

        // Scales the look axes (X and Y) of every Cinemachine input controller in the loaded scenes.
        private static void ApplyLook()
        {
            if (!Application.isPlaying)
                return;

            foreach (CinemachineInputAxisController input in
                     UnityEngine.Object.FindObjectsByType<CinemachineInputAxisController>(FindObjectsInactive.Include))
            {
                List<InputAxisControllerBase<CinemachineInputAxisController.Reader>.Controller> controllers = input.Controllers;
                if (!baseGains.TryGetValue(input, out float[] gains) || gains.Length != controllers.Count)
                {
                    gains = new float[controllers.Count];
                    for (int i = 0; i < controllers.Count; i++)
                        gains[i] = controllers[i].Input.Gain;
                    baseGains[input] = gains;
                }

                for (int i = 0; i < controllers.Count; i++)
                {
                    string axis = controllers[i].Name ?? string.Empty;
                    bool isX = axis.EndsWith("X", StringComparison.OrdinalIgnoreCase);
                    bool isY = axis.EndsWith("Y", StringComparison.OrdinalIgnoreCase);
                    if (!isX && !isY)
                        continue; // e.g. zoom: left as designed

                    controllers[i].Input.Gain = gains[i] * lookSensitivity * (isY && invertY ? -1f : 1f);
                }
            }
        }
    }
}
