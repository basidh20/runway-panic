using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.Core
{
    /// <summary>
    /// Moving between the main menu and the game. Both scenes must be in the build's scene list (the main menu
    /// first). Always restores normal time, so a paused or finished game never leaves the next scene frozen.
    /// </summary>
    public static class SceneFlow
    {
        public const string MainMenuScene = "MainMenu";
        public const string GameScene = "Main";

        /// <summary>True when the main menu scene is in the build, so "Main Menu" buttons have somewhere to go.</summary>
        public static bool HasMainMenu => Application.CanStreamedLevelBeLoaded(MainMenuScene);

        /// <summary>Loads the main menu. Check <see cref="HasMainMenu"/> first (screens hide the button without one).</summary>
        public static void LoadMainMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(MainMenuScene);
        }

        /// <summary>Starts loading the game in the background; watch the returned operation's progress.</summary>
        public static AsyncOperation LoadGameAsync()
        {
            Time.timeScale = 1f;
            return SceneManager.LoadSceneAsync(GameScene);
        }
    }
}
