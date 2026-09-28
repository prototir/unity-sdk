using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Prototir.Editor
{
    /// <summary>Publish to Prototir: build, upload, and open the website with the build already
    /// attached, where the creator finishes the details and publishes (§16.5.35).
    ///
    /// <para>The editor never publishes on its own. Rights confirmation, visibility, cover and
    /// release notes belong in the form the creator already knows; this only removes the part
    /// where they find the ZIP and drag it into a browser.</para>
    ///
    /// <para>When the project already has a slug (Prototir > Create Settings), the browser opens
    /// that prototype's Studio page instead, to replace its web build or add this native one.</para></summary>
    public static class PrototirPublish
    {
        private const string Title = "Publish to Prototir";

        [MenuItem("Prototir/Publish to Prototir (Web)", priority = 10)]
        public static void PublishWeb()
        {
            if (!CanRun() || !PrototirExport.CanBuildWeb()) return;
            Run(BuildTarget.WebGL, "prototir-web");
        }

        [MenuItem("Prototir/Publish to Prototir (Native)", priority = 11)]
        public static void PublishNative()
        {
            if (!CanRun() || !PrototirExport.CanBuildNative(out var target)) return;
            if (target == BuildTarget.StandaloneWindows)
            {
                PrototirExport.Fail("Prototir hosts 64-bit Windows builds. Choose x86_64 in File > Build Profiles, then publish again.");
                return;
            }
            Run(target, $"prototir-{PrototirProjectSetup.Describe(target).ToLowerInvariant()}");
        }

        private static bool CanRun()
        {
            if (!Application.isBatchMode) return true;
            // Linking needs a person at a browser, which a batch run does not have.
            Debug.LogError("Prototir: Publish needs the editor UI. Use Export for Prototir in batch builds.");
            return false;
        }

        private static void Run(BuildTarget target, string archiveName)
        {
            var apiBase = PrototirEditorLink.ApiBase();
            try
            {
                // Linked first: finding out the editor is not linked after a ten-minute build
                // is the one order worse than asking up front.
                var link = EnsureLinked(apiBase);
                if (link == null) return;

                var parent = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "PrototirPublish");
                Directory.CreateDirectory(parent);
                var archive = PrototirExport.Build(target, parent, archiveName, announce: false);
                if (archive == null) return;

                var claim = Upload(apiBase, link, archive);
                if (claim == null) return;

                var settings = PrototirSettings.Load();
                var slug = settings != null && settings.IsConfigured ? settings.PrototypeSlug : null;
                var native = target != BuildTarget.WebGL;
                Wait(async ct =>
                {
                    await PrototirDirectUpload.RegisterAsync(apiBase, link.Token, claim, Path.GetFileName(archive),
                        native ? "native" : "web", native ? Platform(target) : null,
                        native ? Architecture(target) : null, slug, ct).ConfigureAwait(false);
                    return true;
                }, "Finishing the upload...");

                Application.OpenURL(DestinationUrl(link.AppOrigin, slug));
                Debug.Log($"Prototir: uploaded {Path.GetFileName(archive)}. Finish publishing in your browser.");
            }
            catch (Exception error)
            {
                PrototirExport.Fail(error is UnauthorizedAccessException
                    ? "This editor is no longer linked to your Prototir account. Publish again to link it."
                    : $"Publishing stopped: {error.Message}");
                if (error is UnauthorizedAccessException) PrototirEditorLink.Forget(apiBase);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static PrototirEditorLink.Link EnsureLinked(string apiBase)
        {
            var stored = PrototirEditorLink.Stored(apiBase);
            if (stored != null)
            {
                var checkedLink = Wait(ct => PrototirEditorLink.VerifyAsync(apiBase, stored, ct), "Checking this editor's link...");
                if (checkedLink != null) return checkedLink;
                PrototirEditorLink.Forget(apiBase);
            }

            if (!EditorUtility.DisplayDialog(Title,
                    "Link this editor to your Prototir account first. Your browser will open to approve it, " +
                    "once per computer.\n\nThe link can only upload builds: every upload still waits for you " +
                    "on the website before anything is published.",
                    "Open browser", "Cancel"))
                return null;

            var label = PrototirEditorLink.Label();
            var pending = Wait(ct => PrototirEditorLink.StartAsync(apiBase, label, ct), "Asking Prototir for a code...");
            Application.OpenURL(pending.VerificationUrl);
            var link = Wait(ct => PrototirEditorLink.WaitForApprovalAsync(apiBase, pending, ct),
                $"Approve this editor in your browser. Code: {pending.Code}");
            PrototirEditorLink.Remember(apiBase, link);
            return link;
        }

        private static string Upload(string apiBase, PrototirEditorLink.Link link, string archive)
        {
            var total = new FileInfo(archive).Length;
            long sent = 0;
            return Wait(
                ct => PrototirDirectUpload.UploadAsync(apiBase, link.Token, archive, n => Interlocked.Add(ref sent, n), ct),
                () => $"Uploading {Path.GetFileName(archive)} ({Interlocked.Read(ref sent) / 1048576d:0.0} of {total / 1048576d:0.0} MB)",
                () => total == 0 ? 1f : (float)Interlocked.Read(ref sent) / total);
        }

        /// <summary>Runs <paramref name="work"/> off the main thread and keeps the editor showing a
        /// cancelable progress bar until it finishes. Blocking the editor is deliberate: a build is
        /// about to be uploaded and nothing else in the project should change underneath it.</summary>
        private static T Wait<T>(Func<CancellationToken, Task<T>> work, string info) =>
            Wait(work, () => info, () => -1f);

        private static T Wait<T>(Func<CancellationToken, Task<T>> work, Func<string> info, Func<float> progress)
        {
            using var cts = new CancellationTokenSource();
            var task = Task.Run(() => work(cts.Token));
            while (!task.IsCompleted)
            {
                var fraction = progress();
                // Indeterminate waits (linking, checking) pulse rather than sit at zero.
                if (fraction < 0) fraction = (float)(EditorApplication.timeSinceStartup % 2d / 2d);
                if (EditorUtility.DisplayCancelableProgressBar(Title, info(), fraction)) cts.Cancel();
                Thread.Sleep(50);
            }
            EditorUtility.ClearProgressBar();
            if (task.IsCanceled || (task.IsFaulted && cts.IsCancellationRequested))
                throw new OperationCanceledException("Cancelled.");
            return task.GetAwaiter().GetResult();
        }

        /// <summary>Where the creator finishes. The upload itself is registered with Prototir,
        /// so the page lists it on its own; the address only says which page to open.</summary>
        internal static string DestinationUrl(string origin, string slug) =>
            string.IsNullOrEmpty(slug)
                ? $"{origin}/dashboard/upload?title={Uri.EscapeDataString(PlayerSettings.productName ?? string.Empty)}"
                : $"{origin}/dashboard/{Uri.EscapeDataString(slug)}/edit#builds";

        private static string Platform(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneOSX => "macos",
            BuildTarget.StandaloneLinux64 => "linux",
            _ => "windows",
        };

        /// <summary>Unity's default macOS player is universal (Intel and Apple Silicon), and its
        /// Windows and Linux players are x86-64. A Windows ARM64 or single-architecture macOS build
        /// is labelled with these defaults; use Export and the upload page to label it exactly.</summary>
        private static string Architecture(BuildTarget target) =>
            target == BuildTarget.StandaloneOSX ? "universal" : "x64";
    }
}
