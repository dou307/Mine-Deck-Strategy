using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class MineDeckBuildAutomation
{
    public static void BuildWindowsDevelopment()
    {
        string outputPath = Environment.GetEnvironmentVariable("MINE_DECK_E2E_BUILD_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = "Builds/E2E/MineDeckStrategyE2E.exe";

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes were found in Build Settings.");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Development build failed: {report.summary.result}");

        UnityEngine.Debug.Log(
            $"[MineDeckBuildAutomation] Development build succeeded: {report.summary.outputPath}");
    }
}
