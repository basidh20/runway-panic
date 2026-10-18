using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Pause, Game Over and Victory screens. Each is shown automatically when the GameManager enters
    /// that state. Restart reloads the current scene; Main Menu returns to the main menu scene (hidden when the build
    /// has none); Quit leaves the game (or stops Play mode in the Editor). The pause screen also opens the shared
    /// settings panel, which Esc / Start closes without unpausing. Keyboard and gamepad focus the first button.
    /// </summary>
    [DefaultExecutionOrder(-50)] // before the GameManager, so closing settings can consume the Esc press
    public sealed class MenuScreensUI : MonoBehaviour
    {
        [Header("Screens")]
        [SerializeField] private GameObject pauseScreen;
        [SerializeField] private GameObject gameOverScreen;
        [SerializeField] private GameObject victoryScreen;
        [SerializeField] private Text gameOverDetail;
        [SerializeField] private Text victoryDetail;

        [Header("Buttons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button[] restartButtons = System.Array.Empty<Button>();
        [SerializeField] private Button[] mainMenuButtons = System.Array.Empty<Button>();
        [SerializeField] private Button[] quitButtons = System.Array.Empty<Button>();

        [Header("Settings")]
        [SerializeField] private Button settingsButton;
        [SerializeField] private SettingsPanelUI settingsPanel;

        private GameManager gameManager;

        private void Awake()
        {
            UiEventSystem.Ensure();

            resumeButton.onClick.AddListener(() => gameManager?.Resume());
            foreach (Button button in restartButtons)
                button.onClick.AddListener(Restart);
            foreach (Button button in mainMenuButtons)
            {
                button.onClick.AddListener(SceneFlow.LoadMainMenu);
                button.gameObject.SetActive(SceneFlow.HasMainMenu);
            }
            foreach (Button button in quitButtons)
                button.onClick.AddListener(Quit);

            if (settingsButton != null && settingsPanel != null)
            {
                settingsButton.onClick.AddListener(OpenSettings);
                settingsPanel.Closed += CloseSettings;
                settingsPanel.gameObject.SetActive(false);
            }

            ShowFor(GameState.Playing);
        }

        private void Update()
        {
            // The GameManager may sit on this object or be created separately; connect once it exists.
            if (gameManager == null && GameManager.Instance != null)
            {
                gameManager = GameManager.Instance;
                gameManager.StateChanged += HandleStateChanged;
                ShowFor(gameManager.CurrentState);
            }

            if (settingsPanel != null && settingsPanel.IsOpen && BackPressed())
            {
                gameManager?.ConsumePauseInput();
                settingsPanel.Close();
            }
        }

        private void OnDestroy()
        {
            if (gameManager != null)
                gameManager.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(GameState previous, GameState current) => ShowFor(current);

        private void ShowFor(GameState state)
        {
            if (settingsPanel != null && state != GameState.Paused)
                settingsPanel.gameObject.SetActive(false);

            pauseScreen.SetActive(state == GameState.Paused);
            gameOverScreen.SetActive(state == GameState.GameOver);
            victoryScreen.SetActive(state == GameState.Victory);

            int coins = CoinManager.Instance != null ? CoinManager.Instance.Coins : 0;
            int wave = gameManager != null ? gameManager.CurrentWave : 0;
            int totalWaves = gameManager != null ? gameManager.TotalWaves : 4;

            if (state == GameState.GameOver && gameOverDetail != null)
                gameOverDetail.text = wave > 0
                    ? $"The birds took the runway.\nYou reached wave {wave} of {totalWaves} with {coins} coins."
                    : "The birds took the runway.";

            if (state == GameState.Victory && victoryDetail != null)
                victoryDetail.text = $"All {totalWaves} waves cleared.\nThe airport is open again - {coins} coins left.";

            GameObject shown = state == GameState.Paused ? pauseScreen
                : state == GameState.GameOver ? gameOverScreen
                : state == GameState.Victory ? victoryScreen
                : null;
            FocusFirstButton(shown);
        }

        private void OpenSettings()
        {
            pauseScreen.SetActive(false);
            settingsPanel.Open();
        }

        private void CloseSettings()
        {
            if (gameManager == null || !gameManager.IsPaused)
                return;
            pauseScreen.SetActive(true);
            FocusFirstButton(pauseScreen, settingsButton);
        }

        // Keyboard and gamepad players need a selected button to press.
        private static void FocusFirstButton(GameObject screen, Selectable preferred = null)
        {
            if (screen == null || EventSystem.current == null)
                return;
            Selectable target = preferred != null && preferred.gameObject.activeInHierarchy
                ? preferred
                : screen.GetComponentInChildren<Button>();
            if (target != null)
                EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private static bool BackPressed() =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && (Gamepad.current.startButton.wasPressedThisFrame || Gamepad.current.buttonEast.wasPressedThisFrame));

        private static void Restart()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }

#if UNITY_EDITOR
            // Sandbox scenes are not in the build list; reload them through the Editor instead.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
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
