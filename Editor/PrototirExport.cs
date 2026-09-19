using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Prototir.Editor
{
    /// <summary>One button per delivery mode (D43), so a creator does not have to know which Unity
    /// build settings Prototir expects.
    ///
    /// <para>Both produce a ZIP ready to drop on the upload page: web bundles are uploaded as one
    /// archive, and a desktop build has to be an archive because an executable alone leaves its
    /// data folder behind, which is the most common way a download arrives broken.</para></summary>
    public static class PrototirExport
    {
        [MenuItem("Prototir/Export for Prototir (Web)", priority = 20)]
        public static void ExportWeb()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Fail("Web Build Support is not installed for this Unity version. Add it in Unity Hub.");
                return;
            }

            // Everything the sandbox requires is checked here rather than after a long build, so a
            // missing setting costs a dialog instead of ten minutes.
            var errors = PrototirProjectSetup.FindIssues()
                .Where(issue => issue.Severity == PrototirIssueSeverity.Error)
                .ToArray();
            if (errors.Length > 0)
            {
                Fail("Fix these in Prototir > Project Setup first:\n\n" +
                     string.Join("\n", errors.Select(issue => $"- {issue.Title}")));
                return;
            }

            Export(BuildTarget.WebGL, "Export for Prototir (Web)", "prototir-web");
        }

        [MenuItem("Prototir/Export for Prototir (Download)", priority = 21)]
        public static void ExportDownload()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (!IsDesktop(target))
            {
                // Switching platforms reimports every asset in the project, which can take a very
                // long time. That is the creator's decision to make, not a side effect of clicking
                // a menu item.
                Fail(
                    $"The active build target is {target}. Switch to Windows, macOS or Linux in " +
                    "File > Build Profiles first.\n\nThis button does not switch for you: changing " +
                    "platform reimports the whole project.");
                return;
            }

            Export(target, $"Export for Prototir ({Describe(target)})", $"prototir-{Describe(target).ToLowerInvariant()}");
        }

        private static void Export(BuildTarget target, string title, string archiveName)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                Fail("No scenes are enabled in Build Settings, so there is nothing to export.");
                return;
            }

            var parent = EditorUtility.SaveFolderPanel(title, string.Empty, string.Empty);
            if (string.IsNullOrEmpty(parent)) return;

            // A clean folder per export: leftovers from a previous build ship inside the archive
            // and are impossible to spot once it is uploaded.
            var output = Path.Combine(parent, archiveName);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
                locationPathName = target == BuildTarget.WebGL
                    ? output
                    : Path.Combine(output, ExecutableName(target)),
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Fail($"The build did not finish ({report.summary.result}). See the Console for details.");
                return;
            }

            var archive = output + ".zip";
            try
            {
                if (File.Exists(archive)) File.Delete(archive);
                // Fully qualified: UnityEngine has a CompressionLevel of its own, so the bare
                // name is ambiguous inside an editor script.
                ZipFile.CreateFromDirectory(
                    output, archive, System.IO.Compression.CompressionLevel.Optimal, false);
            }
            catch (Exception error)
            {
                // The build itself is fine and can be zipped by hand, so this is a note, not a
                // failure that should make a creator think the export was wasted.
                Debug.LogWarning($"Prototir: the build succeeded but could not be zipped ({error.Message}). " +
                                 $"Zip {output} yourself before uploading.");
                EditorUtility.RevealInFinder(output);
                return;
            }

            var megabytes = new FileInfo(archive).Length / 1024d / 1024d;
            Debug.Log($"Prototir: exported {archive} ({megabytes:0.0} MB). Upload it at prototir.com.");
            EditorUtility.DisplayDialog(
                "Ready to upload",
                $"{Path.GetFileName(archive)}\n{megabytes:0.0} MB\n\n" +
                (target == BuildTarget.WebGL
                    ? "Add it under \"Play in the browser\" on the upload page."
                    : "Add it under \"Download and run\" on the upload page, and remember a cover " +
                      "image is required when there is no web build."),
                "Show me");
            EditorUtility.RevealInFinder(archive);
        }

        private static bool IsDesktop(BuildTarget target) =>
            target == BuildTarget.StandaloneWindows64
            || target == BuildTarget.StandaloneWindows
            || target == BuildTarget.StandaloneOSX
            || target == BuildTarget.StandaloneLinux64;

        private static string Describe(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows64 or BuildTarget.StandaloneWindows => "Windows",
            BuildTarget.StandaloneOSX => "macOS",
            BuildTarget.StandaloneLinux64 => "Linux",
            _ => target.ToString(),
        };

        private static string ExecutableName(BuildTarget target)
        {
            var product = string.IsNullOrWhiteSpace(Application.productName)
                ? "Game"
                : string.Concat(Application.productName.Split(Path.GetInvalidFileNameChars()));
            return target switch
            {
                BuildTarget.StandaloneWindows64 or BuildTarget.StandaloneWindows => product + ".exe",
                BuildTarget.StandaloneOSX => product + ".app",
                _ => product,
            };
        }

        private static void Fail(string message) =>
            EditorUtility.DisplayDialog("Prototir export", message, "OK");
    }
}
