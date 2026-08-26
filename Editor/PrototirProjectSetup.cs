using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Prototir.Editor
{
    internal enum PrototirIssueSeverity
    {
        Warning,
        Error
    }

    internal sealed class PrototirSetupIssue
    {
        internal string Id { get; }
        internal string Title { get; }
        internal string Detail { get; }
        internal PrototirIssueSeverity Severity { get; }
        internal Action Fix { get; }

        internal PrototirSetupIssue(
            string id,
            string title,
            string detail,
            PrototirIssueSeverity severity,
            Action fix = null)
        {
            Id = id;
            Title = title;
            Detail = detail;
            Severity = severity;
            Fix = fix;
        }
    }

    /// <summary>Checks and repairs settings required by the supported Prototir Unity Web profile.</summary>
    [InitializeOnLoad]
    public sealed class PrototirProjectSetup : EditorWindow
    {
        private const string SessionWarningKey = "Prototir.ProjectSetup.WarningShown";
        private const string PrototirTemplateId = "PROJECT:Prototir";
        private const string PrototirTemplateRelativePath = "Assets/WebGLTemplates/Prototir";
        private Vector2 scroll;

        static PrototirProjectSetup()
        {
            EditorApplication.delayCall += ReportSetupOnce;
        }

        [MenuItem("Prototir/Project Setup", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<PrototirProjectSetup>("Prototir Setup");
            window.minSize = new Vector2(440, 320);
            window.Show();
        }

        internal static IReadOnlyList<PrototirSetupIssue> FindIssues()
        {
            var issues = new List<PrototirSetupIssue>();
            var major = ParseMajorVersion(Application.unityVersion);
            if (major < 6000)
            {
                issues.Add(new PrototirSetupIssue(
                    "unity-version",
                    "Unity 6 is required",
                    $"This project uses Unity {Application.unityVersion}. The first supported profile is Unity 6 Web.",
                    PrototirIssueSeverity.Error));
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                issues.Add(new PrototirSetupIssue(
                    "web-module",
                    "Web Build Support is missing",
                    "Install Web Build Support for this editor version through Unity Hub.",
                    PrototirIssueSeverity.Error));
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                issues.Add(new PrototirSetupIssue(
                    "build-target",
                    "Web is not the active build target",
                    "Switching now avoids a long platform switch when the first Prototir build starts.",
                    PrototirIssueSeverity.Warning,
                    () => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL)));
            }

            if (!EditorBuildSettings.scenes.Any(scene => scene.enabled && File.Exists(scene.path)))
            {
                var activeScenePath = SceneManager.GetActiveScene().path;
                issues.Add(new PrototirSetupIssue(
                    "build-scene",
                    "No enabled scene will be built",
                    string.IsNullOrWhiteSpace(activeScenePath)
                        ? "Save the current scene, then add it to the build profile."
                        : "Add the open scene to the build profile.",
                    PrototirIssueSeverity.Error,
                    string.IsNullOrWhiteSpace(activeScenePath) ? null : AddOpenSceneToBuild));
            }

            if (PlayerSettings.WebGL.threadsSupport)
            {
                issues.Add(new PrototirSetupIssue(
                    "threads",
                    "Native Web threads are enabled",
                    "The standard Prototir sandbox does not provide cross-origin isolation. Disable threads.",
                    PrototirIssueSeverity.Error,
                    () => PlayerSettings.WebGL.threadsSupport = false));
            }

            if (!PlayerSettings.runInBackground)
            {
                issues.Add(new PrototirSetupIssue(
                    "run-in-background",
                    "Run In Background is disabled",
                    "Prototir controls such as fullscreen and restart move focus outside the Unity iframe. Keep the Web player running so a focus transition cannot leave its WebGL canvas black or frozen.",
                    PrototirIssueSeverity.Error,
                    () => PlayerSettings.runInBackground = true));
            }

            if (EditorUserBuildSettings.development || EditorUserBuildSettings.allowDebugging ||
                EditorUserBuildSettings.connectProfiler || ReadEditorBuildFlag("buildWithDeepProfilingSupport"))
            {
                issues.Add(new PrototirSetupIssue(
                    "development",
                    "Development or profiling options are enabled",
                    "Publish a release build without script debugging, profiler connection, or deep profiling.",
                    PrototirIssueSeverity.Error,
                    DisableDevelopmentOptions));
            }

            if (PlayerSettings.WebGL.debugSymbolMode != WebGLDebugSymbolMode.Off)
            {
                issues.Add(new PrototirSetupIssue(
                    "debug-symbols",
                    "Web debug symbols are enabled",
                    "Debug symbols increase the bundle and are rejected by the standard release profile.",
                    PrototirIssueSeverity.Error,
                    () => PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off));
            }

            if ((PlayerSettings.WebGL.template ?? string.Empty).IndexOf("PWA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                issues.Add(new PrototirSetupIssue(
                    "pwa-template",
                    "A PWA Web template is selected",
                    "Service workers and installable PWA output are outside the standard Prototir profile.",
                    PrototirIssueSeverity.Error,
                    () => PlayerSettings.WebGL.template = "APPLICATION:Default"));
            }

            if (!string.Equals(PlayerSettings.WebGL.template, PrototirTemplateId, StringComparison.Ordinal)
                || !File.Exists(Path.Combine(PrototirTemplateDirectory, "index.html")))
            {
                issues.Add(new PrototirSetupIssue(
                    "web-template",
                    "The edge-to-edge Prototir Web template is not selected",
                    "Unity's default template adds its own card, footer, title, and fullscreen control. Install and select the Prototir template so the canvas fills the player shell.",
                    PrototirIssueSeverity.Warning,
                    InstallAndSelectWebTemplate));
            }

            if (PlayerSettings.WebGL.dataCaching)
            {
                issues.Add(new PrototirSetupIssue(
                    "data-caching",
                    "Unity Data Caching is enabled",
                    "Prototir serves immutable assets through HTTP caching; Unity IndexedDB caching is unreliable in an opaque-origin frame.",
                    PrototirIssueSeverity.Warning,
                    () => PlayerSettings.WebGL.dataCaching = false));
            }

            if (string.IsNullOrWhiteSpace(PlayerSettings.productName))
            {
                issues.Add(new PrototirSetupIssue(
                    "product-name",
                    "Product name is empty",
                    "Set a product name so the generated prototir.json has a useful default title.",
                    PrototirIssueSeverity.Warning));
            }

            return issues;
        }

        internal static void ApplyAllFixes()
        {
            foreach (var issue in FindIssues().Where(issue => issue.Fix != null)) issue.Fix();
            AssetDatabase.SaveAssets();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Prototir Project Setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Checks the active project against the Unity 6 single-threaded Web profile before export.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);

            var issues = FindIssues();
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Ready for a Prototir Web release build.", MessageType.Info);
            }
            else
            {
                var errors = issues.Count(issue => issue.Severity == PrototirIssueSeverity.Error);
                var warnings = issues.Count - errors;
                EditorGUILayout.HelpBox(
                    $"{errors} blocking issue(s), {warnings} recommendation(s).",
                    errors > 0 ? MessageType.Error : MessageType.Warning);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (var issue in issues) DrawIssue(issue);
                EditorGUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh")) Repaint();
            using (new EditorGUI.DisabledScope(!issues.Any(issue => issue.Fix != null)))
            {
                if (GUILayout.Button("Fix all available"))
                {
                    ApplyAllFixes();
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawIssue(PrototirSetupIssue issue)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var prefix = issue.Severity == PrototirIssueSeverity.Error ? "BLOCKING" : "RECOMMENDED";
            EditorGUILayout.LabelField($"{prefix}  {issue.Title}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(issue.Detail, EditorStyles.wordWrappedLabel);
            if (issue.Fix != null && GUILayout.Button("Fix"))
            {
                issue.Fix();
                AssetDatabase.SaveAssets();
                Repaint();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private static void ReportSetupOnce()
        {
            if (SessionState.GetBool(SessionWarningKey, false)) return;
            SessionState.SetBool(SessionWarningKey, true);
            var issues = FindIssues();
            if (issues.Count == 0) return;
            var blocking = issues.Count(issue => issue.Severity == PrototirIssueSeverity.Error);
            Debug.LogWarning(
                $"Prototir setup found {blocking} blocking issue(s) and {issues.Count - blocking} recommendation(s). " +
                "Open Prototir > Project Setup to review or fix them.");
        }

        private static int ParseMajorVersion(string version)
        {
            var segment = (version ?? string.Empty).Split('.')[0];
            return int.TryParse(segment, out var major) ? major : 0;
        }

        private static void AddOpenSceneToBuild()
        {
            var path = SceneManager.GetActiveScene().path;
            if (string.IsNullOrWhiteSpace(path)) return;
            var scenes = EditorBuildSettings.scenes.ToList();
            var existing = scenes.FindIndex(scene => scene.path == path);
            if (existing >= 0) scenes[existing] = new EditorBuildSettingsScene(path, true);
            else scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void DisableDevelopmentOptions()
        {
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            WriteEditorBuildFlag("buildWithDeepProfilingSupport", false);
        }

        private static string PrototirTemplateDirectory =>
            Path.Combine(Application.dataPath, "WebGLTemplates", "Prototir");

        private static void InstallAndSelectWebTemplate()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(PrototirProjectSetup).Assembly);
            var source = package == null
                ? null
                : Path.Combine(package.resolvedPath, "Editor", "WebGLTemplates", "Prototir");
            if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
                throw new InvalidOperationException("The Prototir Web template is missing from the installed SDK package.");

            Directory.CreateDirectory(PrototirTemplateDirectory);
            foreach (var sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (sourceFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                var relative = sourceFile.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destination = Path.Combine(PrototirTemplateDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? PrototirTemplateDirectory);
                File.Copy(sourceFile, destination, true);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PlayerSettings.WebGL.template = PrototirTemplateId;
            Debug.Log($"Installed and selected the Prototir Web template at {PrototirTemplateRelativePath}.");
        }

        private static bool ReadEditorBuildFlag(string name)
        {
            var property = typeof(EditorUserBuildSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            return property?.PropertyType == typeof(bool) && property.GetValue(null) is bool value && value;
        }

        private static void WriteEditorBuildFlag(string name, bool value)
        {
            var property = typeof(EditorUserBuildSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property?.CanWrite == true && property.PropertyType == typeof(bool)) property.SetValue(null, value);
        }
    }
}
