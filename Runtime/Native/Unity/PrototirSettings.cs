using System;
using System.IO;
using UnityEngine;

namespace Prototir
{
    /// <summary>Where a downloadable build learns which prototype it is.
    ///
    /// <para>A Web export has none of this: the page it runs in already knows the prototype, and
    /// the browser path keeps taking its context from there. A download has no page, so the slug
    /// has to travel inside the build.</para>
    ///
    /// <para>Loaded from <c>Resources</c> so it ships automatically, without the creator having
    /// to remember to reference it from a scene.</para></summary>
    public sealed class PrototirSettings : ScriptableObject
    {
        public const string ResourceName = "PrototirSettings";
        /// <summary>Where a downloadable build talks to Prototir.
        ///
        /// <para>Its own hostname, not <c>prototir.com/api</c>: the site serves no <c>/api</c>
        /// path, so that default reached nothing and the whole native path was dead in
        /// production until a real build was run against it. Not the Azure hostname behind it
        /// either, which carries a generated id that changes if the app is recreated. This URL is
        /// compiled into shipped executables that can never be updated, so it has to outlive the
        /// infrastructure under it.</para></summary>
        public const string DefaultApiBaseUrl = "https://api.prototir.com/api";

        [Tooltip("The slug in your prototype's URL: prototir.com/p/<slug>.")]
        [SerializeField] private string prototypeSlug = string.Empty;

        [Tooltip("Leave as is unless you are pointing a build at a local Prototir.")]
        [SerializeField] private string apiBaseUrl = DefaultApiBaseUrl;

        [Tooltip("Shown to the tester when they approve this build, so they can tell it apart " +
                 "from someone else's attempt to pair against their account.")]
        [SerializeField] private string deviceLabel = string.Empty;

        public string PrototypeSlug => prototypeSlug?.Trim();

        public string ApiBaseUrl =>
            string.IsNullOrWhiteSpace(apiBaseUrl) ? DefaultApiBaseUrl : apiBaseUrl.Trim();

        /// <summary>Falls back to the machine name, which is what a tester approving from their
        /// phone needs in order to recognise the request as their own.</summary>
        public string DeviceLabel =>
            string.IsNullOrWhiteSpace(deviceLabel) ? SystemInfo.deviceName : deviceLabel.Trim();

        public bool IsConfigured => !string.IsNullOrWhiteSpace(prototypeSlug);

        /// <summary>Settings built in code, for a game that learns its prototype at runtime rather
        /// than at author time: a launcher that passes it in, a build shared across several
        /// prototypes, or a test that wants to point somewhere other than production.</summary>
        public static PrototirSettings Create(
            string prototypeSlug, string apiBaseUrl = null, string deviceLabel = null)
        {
            var settings = CreateInstance<PrototirSettings>();
            settings.prototypeSlug = prototypeSlug ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(apiBaseUrl)) settings.apiBaseUrl = apiBaseUrl;
            if (!string.IsNullOrWhiteSpace(deviceLabel)) settings.deviceLabel = deviceLabel;
            return settings;
        }

        /// <summary>Written beside the executable by Prototir when the build is uploaded. See
        /// <see cref="ReadInjectedSlug"/> for why this beats the asset.</summary>
        public const string InjectedFileName = "prototir-prototype.json";

        /// <summary>Returns null when neither the injected file nor the asset says which prototype
        /// this is. Callers report that as configuration the creator still has to do, rather than
        /// throwing inside someone's game.</summary>
        public static PrototirSettings Load()
        {
            var asset = Resources.Load<PrototirSettings>(ResourceName);
            var injected = ReadInjectedSlug();
            if (string.IsNullOrEmpty(injected)) return asset;
            if (asset == null) return Create(injected);
            if (injected == asset.PrototypeSlug) return asset;

            // The injected slug wins, but everything else the creator set is theirs and is kept:
            // an apiBaseUrl pointing at a local Prototir is the whole reason someone edits this.
            return Create(injected, asset.apiBaseUrl, asset.deviceLabel);
        }

        /// <summary>The slug Prototir put in the build at upload.
        ///
        /// <para><b>Why this outranks the asset.</b> The slug does not exist until the prototype
        /// does, and the prototype does not exist until a build has been uploaded to it, so the
        /// first export a creator makes cannot possibly contain the right value. Prototir knows it
        /// at upload and writes it in, which means a downloaded build reports back with nothing
        /// set by hand. Where the two disagree, the one that travelled with this exact download is
        /// the one describing this exact download.</para>
        ///
        /// <para>Returns null for anything unreadable. A build whose slug file is missing or
        /// damaged falls back to the asset, which is the behaviour every build had before.</para></summary>
        private static string ReadInjectedSlug()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // A Web export takes its prototype from the page it is embedded in, and the browser
            // has no filesystem to read this from.
            return null;
#else
            try
            {
                // dataPath is <build>/<Game>_Data, so its parent is the folder holding the
                // executable, which is where the archive carried the file.
                var root = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(root)) return null;
                var path = Path.Combine(root, InjectedFileName);
                if (!File.Exists(path)) return null;

                var parsed = JsonUtility.FromJson<InjectedConfig>(File.ReadAllText(path));
                var slug = parsed?.slug?.Trim();
                return string.IsNullOrEmpty(slug) ? null : slug;
            }
            catch (Exception)
            {
                return null;
            }
#endif
        }

        [Serializable]
        private sealed class InjectedConfig
        {
            public string slug;
        }
    }
}
