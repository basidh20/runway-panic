using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Pause, Game Over and Victory screens. Each is shown automatically when the GameManager enters
    /// that state. Restart reloads the current scene; Quit leaves the game (or stops Play mode in the Editor).
    /// </summary>
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
        [SerializeField] private Button[] quitButtons = System.Array.Empty<Button>();

        private GameManager gameManager;

        private void Awake()
        {
            UiEventSystem.Ensure();

            resumeButton.onClick.AddListener(() => gameManager?.Resume());
            foreach (Button button in restartButtons)
                button.onClick.AddListener(Restart);
            foreach (Button button in quitButtons)
                button.onClick.AddListener(Quit);

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
        }

        private void OnDestroy()
        {
            if (gameManager != null)
                gameManager.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(GameState previous, GameState current) => ShowFor(current);

        private void ShowFor(GameState state)
        {
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
        }

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
