using System;
using System.Collections.Generic;

namespace Prototir.Native
{
    /// <summary>Release versions as the SDK's Git tags name them (<c>v0.4.0</c>), for the editor's
    /// update check. Engine-free so the comparison is tested without Unity.
    ///
    /// <para>Only plain <c>vMAJOR.MINOR.PATCH</c> tags count as releases: anything with a suffix is
    /// a preview, never offered as an update.</para></summary>
    public static class PrototirSdkVersion
    {
        public static bool TryParse(string text, out Version version)
        {
            version = null;
            var trimmed = (text ?? string.Empty).Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(1);
            var parts = trimmed.Split('.');
            if (parts.Length != 3) return false;
            var numbers = new int[3];
            for (var i = 0; i < 3; i++)
            {
                if (parts[i].Length == 0 || !int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return false;
                foreach (var c in parts[i]) if (c < '0' || c > '9') return false;
            }
            version = new Version(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        /// <summary>The newest release among <paramref name="tags"/>, as "MAJOR.MINOR.PATCH", or
        /// null when none is a release.</summary>
        public static string Latest(IEnumerable<string> tags)
        {
            Version best = null;
            foreach (var tag in tags ?? Array.Empty<string>())
                if (TryParse(tag, out var version) && (best == null || version > best)) best = version;
            return best?.ToString(3);
        }

        /// <summary>True when <paramref name="candidate"/> is a newer release than
        /// <paramref name="current"/>. False when either cannot be read, so a check never nags about
        /// a version it does not understand.</summary>
        public static bool IsNewer(string candidate, string current) =>
            TryParse(candidate, out var a) && TryParse(current, out var b) && a > b;
    }
}
