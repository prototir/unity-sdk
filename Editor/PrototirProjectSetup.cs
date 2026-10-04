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

    /// <summary>What the creator is building for Prototir. A prototype can ship both, but each
    /// build is one or the other, and the checks that matter are completely different: the Web
    /// profile is about the sandbox, a native build is a desktop player with none of those rules.</summary>
    internal enum PrototirTarget
    {
        Web,
        Native
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

    /// <summary>Checks and repairs the settings a Prototir Web or native build needs.</summary>
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

        private const string TargetKey = "Prototir.ProjectSetup.Target";

        /// <summary>The target this project is set up for, kept in the project's UserSettings
        /// folder so it follows the project rather than the machine. Until someone picks, it is
        /// read off the active build target, which is what the creator last built for.</summary>
        internal static PrototirTarget Target
        {
            get
            {
                var stored = EditorUserSettings.GetConfigValue(TargetKey);
                if (Enum.TryParse(stored, out PrototirTarget target)) return target;
                return IsDesktop(EditorUserBuildSettings.activeBuildTarget) ? PrototirTarget.Native : PrototirTarget.Web;
            }
            set => EditorUserSettings.SetConfigValue(TargetKey, value.ToString());
        }

        internal static IReadOnlyList<PrototirSetupIssue> FindIssues() => FindIssues(Target);

        internal static IReadOnlyList<PrototirSetupIssue> FindIssues(PrototirTarget target)
        {
            var issues = new List<PrototirSetupIssue>();
            var major = ParseMajorVersion(Application.unityVersion);
            if (major < 6000)
            {
                issues.Add(new PrototirSetupIssue(
                    "unity-version",
                    "Unity 6 is required",
                    $"This project uses Unity {Application.unityVersion}. Prototir supports Unity 6 and later.",
                    PrototirIssueSeverity.Error));
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

            if (string.IsNullOrWhiteSpace(PlayerSettings.productName))
            {
                issues.Add(new PrototirSetupIssue(
                    "product-name",
                    "Product name is empty",
                    "Set a product name so the generated prototir.json has a useful default title.",
                    PrototirIssueSeverity.Warning));
            }

            if (target == PrototirTarget.Web) AddWebIssues(issues);
            else AddNativeIssues(issues);

            return issues;
        }

        /// <summary>The standard Web profile: everything here is about running inside the
        /// Prototir sandbox iframe, and none of it applies to a native build.</summary>
        private static void AddWebIssues(List<PrototirSetupIssue> issues)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                issues.Add(new PrototirSetupIssue(
                    "web-module",
                    "Web Build Support is missing",
                    "Install Web Build Support for this editor version through Unity Hub.",
                    PrototirIssueSeverity.Error));
            }

            if (PlayerSettings.WebGL.threadsSupport)
            {
                issues.Add(new PrototirSetupIssue(
                    "threads",
                    "Web threads are enabled",
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
            else if (!TemplateMatchesPackage())
            {
                // The template is copied into Assets, so updating the SDK does not update it.
                issues.Add(new PrototirSetupIssue(
                    "web-template-outdated",
                    "The Prototir Web template is from an older SDK",
                    "The copy in Assets/WebGLTemplates/Prototir differs from the one in the installed SDK. Refresh it to get the current version; edits you made to it are replaced.",
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
        }

        /// <summary>A native build is an ordinary desktop player. The only thing Prototir needs is
        /// that it is built for a desktop platform, and it says so rather than switching: a
        /// platform switch reimports the whole project, which is the creator's call to make.</summary>
        private static void AddNativeIssues(List<PrototirSetupIssue> issues)
        {
            var host = HostDesktopTarget();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, host))
            {
                issues.Add(new PrototirSetupIssue(
                    "native-module",
                    $"{Describe(host)} Build Support is missing",
                    "Install it for this editor version through Unity Hub.",
                    PrototirIssueSeverity.Error));
            }

            if (!IsDesktop(EditorUserBuildSettings.activeBuildTarget))
            {
                issues.Add(new PrototirSetupIssue(
                    "native-target",
                    "The active build target is not Windows, macOS or Linux",
                    $"Native builds are exported for the active desktop target. Fix switches to {Describe(host)}, which reimports the project and can take a while.",
                    PrototirIssueSeverity.Warning,
                    () => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, host)));
            }
        }

        internal static bool IsDesktop(BuildTarget target) =>
            target == BuildTarget.StandaloneWindows64
            || target == BuildTarget.StandaloneWindows
            || target == BuildTarget.StandaloneOSX
            || target == BuildTarget.StandaloneLinux64;

        internal static string Describe(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows64 or BuildTarget.StandaloneWindows => "Windows",
            BuildTarget.StandaloneOSX => "macOS",
            BuildTarget.StandaloneLinux64 => "Linux",
            _ => target.ToString(),
        };

        private static BuildTarget HostDesktopTarget() => Application.platform switch
        {
            RuntimePlatform.OSXEditor => BuildTarget.StandaloneOSX,
            RuntimePlatform.LinuxEditor => BuildTarget.StandaloneLinux64,
            _ => BuildTarget.StandaloneWindows64,
        };

        internal static void ApplyAllFixes()
        {
            foreach (var issue in FindIssues(Target).Where(issue => issue.Fix != null)) issue.Fix();
            AssetDatabase.SaveAssets();
        }

        private void OnEnable() => PrototirUpdateCheck.Checked += Repaint;
        private void OnDisable() => PrototirUpdateCheck.Checked -= Repaint;

        private void DrawUpdate()
        {
            var available = PrototirUpdateCheck.Available;
            if (available == null) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Prototir SDK {available} is available", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                PrototirUpdateCheck.CanUpdate
                    ? $"This project has {PrototirUpdateCheck.Installed}. Update moves it to the new release; Unity then recompiles."
                    : $"This project has {PrototirUpdateCheck.Installed}, installed as a local copy. Replace it with the {available} release from GitHub.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("What's new")) Application.OpenURL(PrototirUpdateCheck.ChangelogUrl(available));
            using (new EditorGUI.DisabledScope(!PrototirUpdateCheck.CanUpdate))
            {
                if (GUILayout.Button($"Update to {available}")) PrototirUpdateCheck.Update(available);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        private void OnGUI()
        {
            DrawUpdate();
            EditorGUILayout.LabelField("Prototir Project Setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Building for", EditorStyles.miniBoldLabel);
            var target = Target;
            var picked = (PrototirTarget)GUILayout.Toolbar((int)target, new[] { "Web", "Native" });
            if (picked != target) Target = target = picked;
            EditorGUILayout.LabelField(
                target == PrototirTarget.Web
                    ? "Plays in the browser on Prototir. Checked against the Unity 6 single-threaded Web profile."
                    : "A Windows, macOS or Linux build people download and run. One prototype can have both.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);

            var issues = FindIssues(target);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    target == PrototirTarget.Web
                        ? "Ready for a Prototir Web release build."
                        : "Ready for a Prototir native build.",
                    MessageType.Info);
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

        private static bool TemplateMatchesPackage()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PrototirProjectSetup).Assembly);
            if (package == null) return true;
            var source = Path.Combine(package.resolvedPath, "Editor", "WebGLTemplates", "Prototir", "index.html");
            var copy = Path.Combine(PrototirTemplateDirectory, "index.html");
            if (!File.Exists(source) || !File.Exists(copy)) return true;
            // Line endings differ by checkout, not by version.
            return File.ReadAllText(source).Replace("\r\n", "\n") == File.ReadAllText(copy).Replace("\r\n", "\n");
        }

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
