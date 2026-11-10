using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLDocsBuild
{
    private const string OutputFolder = "docs";

    [MenuItem("Build/WebGL to docs (GitHub Pages)")]
    public static void Build()
    {
        // GitHub Pages cannot send the Content-Encoding headers Unity's compressed builds need.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;

        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        var outputPath = Path.Combine(projectRoot, OutputFolder);
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, true);
        }

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SkyfortIntro.unity", "Assets/Scenes/SkyfortStrike.unity", "Assets/Scenes/SkyPlatformer.unity" },
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new Exception($"WebGL build failed: {report.summary.result}");
        }

        File.WriteAllText(Path.Combine(outputPath, ".nojekyll"), string.Empty);
        Debug.Log($"WebGL build written to {outputPath}. Commit and push the docs folder to publish.");
    }
}
