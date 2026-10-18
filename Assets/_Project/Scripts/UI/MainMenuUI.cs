using System.Collections;
using RunwayPanic.Audio;
using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// The main menu scene: Play (with a loading screen and tips), How to Play, Settings, Credits and Quit (asks
    /// first). Works with mouse, keyboard (arrows / Enter, Esc to go back) and gamepad (D-pad / stick, A, B); a
    /// highlighted button is always selected so the keyboard never gets lost. Plays the calm menu music.
    /// </summary>
    public sealed class MainMenuUI : MonoBehaviour
    {
        [Header("Main screen")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private Button playButton;
        [SerializeField] private Button howToPlayButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button creditsButton;
        [SerializeField] private Button quitButton;

        [Header("Sub screens")]
        [SerializeField] private GameObject howToPlayPanel;
        [SerializeField] private Button howToPlayBack;
        [SerializeField] private GameObject creditsPanel;
        [SerializeField] private Button creditsBack;
        [SerializeField] private SettingsPanelUI settingsPanel;

        [Header("Quit confirmation")]
        [SerializeField] private GameObject quitPanel;
        [SerializeField] private Button quitYes;
        [SerializeField] private Button quitNo;

        [Header("Loading")]
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private RectTransform loadingFill;
        [SerializeField] private Text loadingPercent;
        [SerializeField] private Text loadingTip;
        [SerializeField, TextArea] private string[] tips =
        {
            "Each bird needs the right gun: Pistol for gulls, Assault Rifle for pigeons, Sniper for crows, RPG for hawks.",
            "The wrong gun does only a tenth of the damage. Switch guns with 1 and 2.",
            "Crows steal medkits and shields and stash them. Follow them to get your loot back.",
            "Hide in a shelter to catch your breath, but crows will wait for you at the door.",
            "Spend your coins in the shop between waves. A new gun is equipped straight away.",
            "Push the movable barricades into place to make cover from diving birds.",
            "The hawk screams before it dives. When you hear it, look up.",
        };

        [Header("Audio")]
        [SerializeField] private SoundCue music;
        [SerializeField] private SoundCue ambience;
        [SerializeField] private SoundCue click;
        [Tooltip("Menu music is louder than in-game, where it sits under the action.")]
        [SerializeField, Min(0f)] private float musicBoost = 2f;

        private SoundLoop musicLoop;
        private SoundLoop ambienceLoop;
        private GameObject lastSelected;
        private bool loading;

        private void Awake()
        {
            UiEventSystem.Ensure();
            playButton.onClick.AddListener(Play);
            howToPlayButton.onClick.AddListener(() => Show(howToPlayPanel, howToPlayBack));
            settingsButton.onClick.AddListener(OpenSettings);
            creditsButton.onClick.AddListener(() => Show(creditsPanel, creditsBack));
            quitButton.onClick.AddListener(() => Show(quitPanel, quitNo));
            howToPlayBack.onClick.AddListener(ShowMain);
            creditsBack.onClick.AddListener(ShowMain);
            quitNo.onClick.AddListener(ShowMain);
            quitYes.onClick.AddListener(Quit);
            settingsPanel.Closed += () => ShowMain(settingsButton);

            foreach (Button button in GetComponentsInChildren<Button>(true))
                button.onClick.AddListener(PlayClick);
            foreach (Toggle toggle in GetComponentsInChildren<Toggle>(true))
                toggle.onValueChanged.AddListener(_ => PlayClick());
        }

        private void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ShowMain(playButton);
            musicLoop = SoundPlayer.StartLoop(music, null, musicBoost);
            ambienceLoop = SoundPlayer.StartLoop(ambience);
        }

        private void OnDestroy() => StopAudio();

        private void Update()
        {
            if (loading)
                return;

            if (BackPressed())
            {
                if (settingsPanel.IsOpen)
                    settingsPanel.Close();
                else if (mainPanel.activeSelf)
                    Show(quitPanel, quitNo);
                else
                    ShowMain();
            }

            KeepSelection();
        }

        private void Play()
        {
            if (loading)
                return;
            StartCoroutine(LoadGame());
        }

        private IEnumerator LoadGame()
        {
            loading = true;
            HideAll();
            loadingPanel.SetActive(true);
            if (tips.Length > 0)
                loadingTip.text = tips[Random.Range(0, tips.Length)];
            SetProgress(0f);

            // Let the tip be read and the music fade a little before the heavy load starts.
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                if (musicLoop != null) musicLoop.VolumeScale = musicBoost * (1f - t / 0.6f);
                yield return null;
            }
            StopAudio();

            AsyncOperation operation = SceneFlow.LoadGameAsync();
            if (operation == null)
            {
                Debug.LogError($"[MainMenu] Scene '{SceneFlow.GameScene}' is not in the build's scene list.");
                loading = false;
                ShowMain(playButton);
                yield break;
            }

            while (!operation.isDone)
            {
                SetProgress(Mathf.Clamp01(operation.progress / 0.9f)); // Unity reports 0.9 when loading is done
                yield return null;
            }
        }

        private void SetProgress(float value)
        {
            loadingFill.anchorMax = new Vector2(value, 1f);
            loadingPercent.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        private void OpenSettings()
        {
            HideAll();
            settingsPanel.Open();
        }

        private void ShowMain() => ShowMain(lastSelected != null ? lastSelected.GetComponent<Button>() : playButton);

        private void ShowMain(Button focus)
        {
            HideAll();
            mainPanel.SetActive(true);
            Select(focus != null ? focus : playButton);
        }

        private void Show(GameObject panel, Button focus)
        {
            // Remember which main button opened it, so Back returns focus there.
            GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (current != null && current.transform.IsChildOf(mainPanel.transform))
                lastSelected = current;

            HideAll();
            panel.SetActive(true);
            Select(focus);
        }

        private void HideAll()
        {
            mainPanel.SetActive(false);
            howToPlayPanel.SetActive(false);
            creditsPanel.SetActive(false);
            quitPanel.SetActive(false);
            loadingPanel.SetActive(false);
            if (settingsPanel.IsOpen)
                settingsPanel.gameObject.SetActive(false);
        }

        // Keyboard and gamepad need something selected; clicking empty space would otherwise leave nothing.
        private void KeepSelection()
        {
            EventSystem events = EventSystem.current;
            if (events == null)
                return;

            GameObject selected = events.currentSelectedGameObject;
            if (selected != null && selected.activeInHierarchy)
                return;

            Selectable first = FindFirstSelectable();
            if (first != null)
                events.SetSelectedGameObject(first.gameObject);
        }

        private Selectable FindFirstSelectable()
        {
            foreach (GameObject panel in new[] { quitPanel, howToPlayPanel, creditsPanel, settingsPanel.gameObject, mainPanel })
                if (panel.activeInHierarchy)
                {
                    Selectable selectable = panel.GetComponentInChildren<Selectable>();
                    if (selectable != null)
                        return selectable;
                }
            return null;
        }

        private static bool BackPressed() =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        private static void Select(Selectable target)
        {
            if (EventSystem.current != null && target != null)
                EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private void PlayClick() => SoundPlayer.Play2D(click);

        private void StopAudio()
        {
            if (musicLoop != null) musicLoop.Stop();
            if (ambienceLoop != null) ambienceLoop.Stop();
            musicLoop = null;
            ambienceLoop = null;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
