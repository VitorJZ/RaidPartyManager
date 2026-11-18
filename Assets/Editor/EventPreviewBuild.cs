using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RaidPartyManager.Editor
{
    public static class EventPreviewBuild
    {
        [MenuItem("Raid Party Manager/Build macOS Preview")]
        public static void BuildMac()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-eventBuildOutput");
            string path = index >= 0 && index + 1 < args.Length ? args[index + 1] :
                Path.GetFullPath("Builds/EventPreview/RaidPartyManager.app");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                target = BuildTarget.StandaloneOSX,
                locationPathName = path,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Preview build failed: " + report.summary.result);
            Debug.Log($"Event preview built at {path}; {report.summary.totalSize} bytes; {report.summary.totalErrors} errors.");
        }
    }
}
