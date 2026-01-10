using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CliBuilder
{
    public static void Build()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/MATE ENGINE - Scenes/Mate Engine Main.unity"},
            locationPathName = "~/Desktop/MateEngineXX/MateEngineX.x86_64",
            target = BuildTarget.StandaloneLinux64,
            options = BuildOptions.CompressWithLz4HC
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
            Debug.Log($"Build succeeded → {summary.totalSize / 1048576f:F1} MB");
        else
        {
            Debug.LogError("Build failed!");
            EditorApplication.Exit(1);
        }
    }
}