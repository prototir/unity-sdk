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
        public const string DefaultApiBaseUrl = "https://prototir.com/api";

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

        /// <summary>Returns null when the asset is missing. Callers report that as configuration
        /// the creator still has to do, rather than throwing inside someone's game.</summary>
        public static PrototirSettings Load() => Resources.Load<PrototirSettings>(ResourceName);
    }
}
