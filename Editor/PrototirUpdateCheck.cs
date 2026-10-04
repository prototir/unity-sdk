using System;
using System.Collections.Generic;
using Prototir.Native;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;

namespace Prototir.Editor
{
    /// <summary>Tells the creator when a newer SDK is out, and updates it with one click.
    ///
    /// <para>The Package Manager never offers updates for a package installed from a Git URL, so
    /// without this a project stays on the SDK it was set up with, and its testers never get new
    /// tools. Checked once a day against the SDK's Git tags. Nothing in the project changes until
    /// the creator presses Update: an update is code changing in their project, possibly the night
    /// before a deadline.</para></summary>
    [InitializeOnLoad]
    public static class PrototirUpdateCheck
    {
        public const string Repository = "https://github.com/prototir/unity-sdk";
        private const string TagsUrl = "https://api.github.com/repos/prototir/unity-sdk/tags?per_page=50";
        private const string LastCheckKey = "Prototir.UpdateCheck.LastUtc";
        private const string LatestKey = "Prototir.UpdateCheck.Latest";
        private const string AnnouncedKey = "Prototir.UpdateCheck.Announced";
        private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
        private static UnityWebRequest _request;
        private static bool _force;

        /// <summary>Raised when a check finishes, so an open Project Setup window can redraw.</summary>
        public static event Action Checked;

        static PrototirUpdateCheck()
        {
            // Not in batch mode: a CI build has nobody to tell, and should not call out to GitHub.
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () => Check(false);
        }

        [MenuItem("Prototir/Check for SDK Updates", priority = 1)]
        private static void CheckNow()
        {
            Check(true);
            PrototirProjectSetup.ShowWindow();
        }

        /// <summary>The installed package version, or null when the SDK is not installed as a package.</summary>
        public static string Installed => Package?.version;

        /// <summary>The newest release seen by the last check, when it is newer than the installed one.</summary>
        public static string Available
        {
            get
            {
                var latest = EditorPrefs.GetString(LatestKey, "");
                return PrototirSdkVersion.IsNewer(latest, Installed) ? latest : null;
            }
        }

        /// <summary>Whether Update can do it here: only a Git install can be moved to another tag.
        /// An embedded or local copy belongs to the creator, who updates it themselves.</summary>
        public static bool CanUpdate => Package?.source == PackageSource.Git && GitUrl != null;

        public static bool Checking => _request != null;

        public static string ChangelogUrl(string version) => $"{Repository}/blob/v{version}/CHANGELOG.md";

        private static UnityEditor.PackageManager.PackageInfo Package =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PrototirUpdateCheck).Assembly);

        /// <summary>The URL the project installed from, without its tag, so a fork stays a fork.</summary>
        private static string GitUrl
        {
            get
            {
                var id = Package?.packageId ?? "";
                var at = id.IndexOf('@');
                if (at < 0) return null;
                var url = id.Substring(at + 1);
                var hash = url.IndexOf('#');
                return hash < 0 ? url : url.Substring(0, hash);
            }
        }

        public static void Check(bool force)
        {
            if (_request != null || Installed == null) return;
            if (!force && DateTime.TryParse(EditorPrefs.GetString(LastCheckKey, ""), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var last) &&
                DateTime.UtcNow - last < Interval)
                return;
            _force = force;
            _request = UnityWebRequest.Get(TagsUrl);
            _request.SetRequestHeader("User-Agent", "prototir-unity-sdk");
            _request.SetRequestHeader("Accept", "application/vnd.github+json");
            _request.timeout = 15;
            _request.SendWebRequest();
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (_request == null || !_request.isDone) return;
            EditorApplication.update -= Poll;
            var request = _request;
            _request = null;
            try
            {
                // A failed check stays quiet and is tried again tomorrow: being offline is not
                // something to report every time the editor opens.
                if (request.result != UnityWebRequest.Result.Success) return;
                EditorPrefs.SetString(LastCheckKey, DateTime.UtcNow.ToString("o"));
                var tags = JsonUtility.FromJson<TagList>("{\"items\":" + request.downloadHandler.text + "}");
                var names = new List<string>();
                foreach (var tag in tags?.items ?? Array.Empty<Tag>()) names.Add(tag.name);
                var latest = PrototirSdkVersion.Latest(names);
                if (latest == null) return;
                EditorPrefs.SetString(LatestKey, latest);
                // Once per version, in the console where the creator already looks.
                if (Available != null && (_force || EditorPrefs.GetString(AnnouncedKey, "") != latest))
                {
                    EditorPrefs.SetString(AnnouncedKey, latest);
                    Debug.Log($"Prototir SDK {latest} is available (this project has {Installed}). " +
                              "Open Prototir > Project Setup to see what is new and update.");
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Prototir: could not read the SDK versions ({error.Message}).");
            }
            finally
            {
                request.Dispose();
                Checked?.Invoke();
            }
        }

        /// <summary>Moves the project to <paramref name="version"/>'s tag. Unity resolves and
        /// recompiles; Project Setup then offers to refresh the Web template copied into Assets.</summary>
        public static void Update(string version)
        {
            if (!CanUpdate) return;
            Client.Add($"{GitUrl}#v{version}");
        }

        [Serializable]
        private sealed class TagList
        {
            public Tag[] items;
        }

        [Serializable]
        private sealed class Tag
        {
            public string name;
        }
    }
}
