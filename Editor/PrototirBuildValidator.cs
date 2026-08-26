using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Prototir.Editor
{
    /// <summary>Rejects build settings and output shapes outside the first supported profile.</summary>
    public sealed class PrototirBuildValidator : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;
            if ((report.summary.options & BuildOptions.Development) != 0)
                throw new BuildFailedException("Prototir accepts Unity Web release builds, not Development builds.");
            var issues = PrototirProjectSetup.FindIssues();
            foreach (var warning in issues.Where(issue => issue.Severity == PrototirIssueSeverity.Warning))
                UnityEngine.Debug.LogWarning($"Prototir setup: {warning.Title}. {warning.Detail}");
            var errors = issues.Where(issue => issue.Severity == PrototirIssueSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new BuildFailedException(
                    "Fix Prototir Project Setup before building:\n- " +
                    string.Join("\n- ", errors.Select(issue => $"{issue.Title}: {issue.Detail}")));
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;
            WriteManifest(report.summary.outputPath);
            ValidateExport(report.summary.outputPath);
        }

        [MenuItem("Prototir/Create or Open Project Manifest")]
        private static void CreateOrOpenProjectManifest()
        {
            var path = ProjectManifestPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.dataPath);
            if (!File.Exists(path)) File.WriteAllText(path, DefaultManifestJson());
            AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Prototir/prototir.json");
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            AssetDatabase.OpenAsset(asset);
        }

        [MenuItem("Prototir/Validate Web Export Folder...")]
        private static void ValidateSelectedExport()
        {
            var path = EditorUtility.OpenFolderPanel("Select Unity Web export", string.Empty, string.Empty);
            if (string.IsNullOrWhiteSpace(path)) return;
            ValidateExport(path);
            EditorUtility.DisplayDialog("Prototir", "The export matches the standard Unity Web profile.", "OK");
        }

        private static void ValidateExport(string exportPath)
        {
            if (!Directory.Exists(exportPath))
                throw new BuildFailedException($"Unity Web output was not found: {exportPath}");
            if (!File.Exists(Path.Combine(exportPath, "index.html")))
                throw new BuildFailedException("Unity Web output must contain index.html at its root.");

            var files = Directory.GetFiles(exportPath, "*", SearchOption.AllDirectories);
            if (!files.Any(path => HasGeneratedExtension(path, ".wasm")))
                throw new BuildFailedException("Unity Web output does not contain a .wasm payload.");
            if (!files.Any(path => HasGeneratedExtension(path, ".data")))
                throw new BuildFailedException("Unity Web output does not contain a .data payload.");
            if (files.Any(path => string.Equals(Path.GetFileName(path), "service-worker.js", StringComparison.OrdinalIgnoreCase)))
                throw new BuildFailedException("PWA/service-worker output is outside the first Prototir runtime profile.");
            if (files.Any(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)))
                throw new BuildFailedException("Remove source maps and debug symbols from the release export.");

            var manifestPath = Path.Combine(exportPath, "prototir.json");
            if (!File.Exists(manifestPath))
                throw new BuildFailedException("The SDK did not produce a root prototir.json.");
            DefaultManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<DefaultManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception exception)
            {
                throw new BuildFailedException($"prototir.json is not valid JSON: {exception.Message}");
            }
            if (manifest?.runtime == null || manifest.runtime.engine != "unity")
                throw new BuildFailedException("prototir.json runtime.engine must be unity.");
            if (!string.IsNullOrWhiteSpace(manifest.runtime.profile) && manifest.runtime.profile != "standard")
                throw new BuildFailedException("Only the standard Unity runtime profile is currently supported.");
            var exportRoot = Path.GetFullPath(exportPath).TrimEnd(Path.DirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
            var entry = string.IsNullOrWhiteSpace(manifest.entry) ? "index.html" : manifest.entry;
            var entryPath = Path.GetFullPath(Path.Combine(exportRoot, entry));
            if (!entryPath.StartsWith(exportRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(entryPath))
                throw new BuildFailedException("prototir.json must reference an entry file present in the export.");
        }

        private static string ProjectManifestPath =>
            Path.Combine(Application.dataPath, "Prototir", "prototir.json");

        private static void WriteManifest(string exportPath)
        {
            var destination = Path.Combine(exportPath, "prototir.json");
            if (File.Exists(ProjectManifestPath))
                File.Copy(ProjectManifestPath, destination, true);
            else
                File.WriteAllText(destination, DefaultManifestJson());
        }

        private static string DefaultManifestJson()
        {
            var manifest = new DefaultManifest
            {
                name = string.IsNullOrWhiteSpace(PlayerSettings.productName)
                    ? "Unity Web prototype"
                    : PlayerSettings.productName,
                runtime = new RuntimeManifest { engineVersion = Application.unityVersion }
            };
            return JsonUtility.ToJson(manifest, true) + Environment.NewLine;
        }

        private static bool HasGeneratedExtension(string path, string extension)
        {
            var file = Path.GetFileName(path).ToLowerInvariant();
            return file.EndsWith(extension) || file.EndsWith(extension + ".gz") ||
                   file.EndsWith(extension + ".br") || file.EndsWith(extension + ".unityweb");
        }

        [Serializable]
        private sealed class DefaultManifest
        {
            public string name;
            public string type = "game";
            public string entry = "index.html";
            public string[] devices = { "desktop", "mobile" };
            public string orientation = "any";
            public RuntimeManifest runtime;
        }

        [Serializable]
        private sealed class RuntimeManifest
        {
            public string engine = "unity";
            public string engineVersion;
            public string profile = "standard";
        }
    }
}
