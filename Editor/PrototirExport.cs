using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Prototir.Editor
{
    /// <summary>One button per build type (D43), Web and Native, so a creator does not have to know
    /// which Unity build settings Prototir expects.
    ///
    /// <para>Both produce a ZIP ready to drop on the upload page: web bundles are uploaded as one
    /// archive, and a native build has to be an archive because an executable alone leaves its
    /// data folder behind, which is the most common way a download arrives broken.</para></summary>
    public static class PrototirExport
    {
        [MenuItem("Prototir/Export for Prototir (Web)", priority = 20)]
        public static void ExportWeb()
        {
            if (!CanBuildWeb()) return;
            var parent = EditorUtility.SaveFolderPanel("Export for Prototir (Web)", string.Empty, string.Empty);
            if (!string.IsNullOrEmpty(parent)) Export(BuildTarget.WebGL, parent, "prototir-web");
        }

        [MenuItem("Prototir/Export for Prototir (Native)", priority = 21)]
        public static void ExportNative()
        {
            if (!CanBuildNative(out var target)) return;

            if (!HasSlug() && !Application.isBatchMode && !EditorUtility.DisplayDialog(
                    "Prototir export",
                    "This project has no prototype slug, so the build will not report sessions and "
                    + "testers will not be able to send feedback from inside it.\n\n"
                    + "That is expected the first time: the slug only exists once the prototype is "
                    + "on Prototir. Upload this build, then use Prototir > Create Settings, fill in "
                    + "the slug from prototir.com/p/<slug>, and export again.",
                    "Export anyway", "Cancel"))
                return;

            var parent = EditorUtility.SaveFolderPanel(
                $"Export for Prototir ({PrototirProjectSetup.Describe(target)})", string.Empty, string.Empty);
            if (!string.IsNullOrEmpty(parent))
                Export(target, parent, $"prototir-{PrototirProjectSetup.Describe(target).ToLowerInvariant()}");
        }

        /// <summary>Everything the sandbox requires, checked before a long build so a missing
        /// setting costs a dialog instead of ten minutes. Shared by Export and Publish.</summary>
        internal static bool CanBuildWeb()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Fail("Web Build Support is not installed for this Unity version. Add it in Unity Hub.");
                return false;
            }

            var errors = PrototirProjectSetup.FindIssues(PrototirTarget.Web)
                .Where(issue => issue.Severity == PrototirIssueSeverity.Error)
                .ToArray();
            if (errors.Length == 0) return true;
            Fail("Fix these in Prototir > Project Setup first:\n\n" +
                 string.Join("\n", errors.Select(issue => $"- {issue.Title}")));
            return false;
        }

        /// <summary>A native build is exported for the active desktop target, never a switched
        /// one: switching platforms reimports every asset in the project, which can take a very
        /// long time. That is the creator's decision to make, not a side effect of a menu item.</summary>
        internal static bool CanBuildNative(out BuildTarget target)
        {
            target = EditorUserBuildSettings.activeBuildTarget;
            if (PrototirProjectSetup.IsDesktop(target)) return true;
            Fail(
                $"The active build target is {target}. Switch to Windows, macOS or Linux in " +
                "File > Build Profiles first.\n\nThis button does not switch for you: changing " +
                "platform reimports the whole project.");
            return false;
        }

        /// <summary>The export itself, with the folder already chosen. Separate from the menu
        /// items so it can be driven headlessly: picking a folder is UI, building and packaging
        /// is not, and only the second half is worth exercising without a human present.</summary>
        public static bool Export(BuildTarget target, string parent, string archiveName) =>
            Build(target, parent, archiveName, announce: true) != null;

        /// <summary>Builds and zips, returning the archive path, or null after reporting why not.
        /// <paramref name="announce"/> is the "Ready to upload" dialog and file browser, which
        /// Publish skips because it uploads the archive itself.</summary>
        internal static string Build(BuildTarget target, string parent, string archiveName, bool announce)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                Fail("No scenes are enabled in Build Settings, so there is nothing to export.");
                return null;
            }

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
                return null;
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
                // A failed CreateFromDirectory leaves a truncated archive behind. Leaving it next
                // to a perfectly good build is how someone uploads half a game, so it goes.
                try { if (File.Exists(archive)) File.Delete(archive); } catch (IOException) { }

                // Windows still caps most paths at 260 characters, and it reports the overrun as
                // a missing file, which sends people looking for the wrong problem entirely.
                var hint = error is DirectoryNotFoundException or PathTooLongException
                    ? " This usually means the path is too long for Windows; export somewhere "
                      + "closer to the drive root."
                    : string.Empty;

                // The build itself is fine and can be zipped by hand, so this is a note, not a
                // failure that should make a creator think the export was wasted.
                Debug.LogWarning($"Prototir: the build succeeded but could not be zipped ({error.Message}). " +
                                 $"Zip {output} yourself before uploading.{hint}");
                if (!Application.isBatchMode) EditorUtility.RevealInFinder(output);
                return null;
            }

            var megabytes = new FileInfo(archive).Length / 1024d / 1024d;
            Debug.Log($"Prototir: exported {archive} ({megabytes:0.0} MB). Upload it at prototir.com.");
            if (!announce || Application.isBatchMode) return archive;

            EditorUtility.DisplayDialog(
                "Ready to upload",
                $"{Path.GetFileName(archive)}\n{megabytes:0.0} MB\n\n" +
                (target == BuildTarget.WebGL
                    ? "On the upload page, choose Add a build > Play in the browser."
                    : $"On the upload page, choose Add a build > {PrototirProjectSetup.Describe(target)}. " +
                      "A native-only prototype also needs a cover image."),
                "Show me");
            EditorUtility.RevealInFinder(archive);
            return archive;
        }

        /// <summary>A native build with no slug still runs; it simply cannot say anything back.</summary>
        private static bool HasSlug()
        {
            var settings = PrototirSettings.Load();
            return settings != null && settings.IsConfigured;
        }

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

        internal static void Fail(string message)
        {
            // A dialog nobody can dismiss would hang a headless build forever.
            if (Application.isBatchMode) Debug.LogError($"Prototir export: {message}");
            else EditorUtility.DisplayDialog("Prototir export", message, "OK");
        }
    }
}
