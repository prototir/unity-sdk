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

        /// <summary>Returns null when the asset is missing. Callers report that as configuration
        /// the creator still has to do, rather than throwing inside someone's game.</summary>
        public static PrototirSettings Load() => Resources.Load<PrototirSettings>(ResourceName);
    }
}
