using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Builds the production macOS player without launching it or including sandboxes.</summary>
    public static class ReleasePlayerBuild
    {
        [MenuItem("Tools/Runway Panic/Build macOS Release Player")]
        public static void BuildMac()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Leave Play mode and wait for compilation/building first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save all open scenes before building.");
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var expected = new[] { "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Main.unity" };
            if (!scenes.SequenceEqual(expected))
                throw new InvalidOperationException("Expected only MainMenu then Main in the enabled global build scenes.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                throw new InvalidOperationException("macOS build support is not installed for this Editor.");

            string folder = Path.GetFullPath("Builds/macOS");
            string player = Path.Combine(folder, "RunwayPanic.app");
            if (Directory.Exists(player) || File.Exists(player))
                throw new InvalidOperationException("A player already exists at " + player +
                    ". Move it aside before building; this command will not overwrite it.");
            Directory.CreateDirectory(folder);
            Debug.Log("[ReleaseBuild] Building macOS player: " + player);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = player,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            });
            var summary = report.summary;
            string result = $"Unity: {Application.unityVersion}\nTarget: {summary.platform}\n" +
                            $"Result: {summary.result}\nErrors: {summary.totalErrors}\nWarnings: {summary.totalWarnings}\n" +
                            $"Duration: {summary.totalTime}\nBytes: {summary.totalSize}\nPlayer: {player}\n" +
                            "Scenes: MainMenu, Main\nManual gameplay test: pending\n";
            File.WriteAllText(Path.Combine(folder, "BuildSummary.txt"), result);
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("[ReleaseBuild] macOS build did not succeed. " + result);
            Debug.Log("[ReleaseBuild] macOS build succeeded. " + result);
        }
    }
}
