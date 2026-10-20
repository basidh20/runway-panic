using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RunwayPanic.UI.EditorTools
{
    /// <summary>
    /// Editor-only helpers for choosing what Unity loads when the normal Play button is pressed.
    /// This changes the local Editor setting only; builds still use the first enabled build scene.
    /// </summary>
    public static class PlayModeStartSceneMenu
    {
        private const string MainMenuPath = "Assets/_Project/Scenes/MainMenu.unity";

        [MenuItem("Tools/Runway Panic/Play Mode/Start at Main Menu")]
        private static void StartAtMainMenu()
        {
            SceneAsset mainMenu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath);
            if (mainMenu == null)
            {
                Debug.LogError($"[Play Mode] Could not find '{MainMenuPath}'.");
                return;
            }

            EditorSceneManager.playModeStartScene = mainMenu;
            Debug.Log("[Play Mode] The Unity Play button will now start at MainMenu.");
        }

        [MenuItem("Tools/Runway Panic/Play Mode/Use Open Scene")]
        private static void UseOpenScene()
        {
            EditorSceneManager.playModeStartScene = null;
            Debug.Log("[Play Mode] The Unity Play button will now use the currently open scene.");
        }

        [MenuItem("Tools/Runway Panic/Play Mode/Start at Main Menu", true)]
        private static bool ValidateStartAtMainMenu() => EditorSceneManager.playModeStartScene == null ||
            EditorSceneManager.playModeStartScene.name != "MainMenu";

        [MenuItem("Tools/Runway Panic/Play Mode/Use Open Scene", true)]
        private static bool ValidateUseOpenScene() => EditorSceneManager.playModeStartScene != null;
    }
}
