using System;
using System.IO;
using UnityEngine;

namespace Prototir.Editor
{
    /// <summary>Writes <c>prototir-build.json</c> into a build output (D43, §16.5.12).
    ///
    /// <para>Prototir records this id from the uploaded archive, and a running build reports the
    /// same id when it pairs. A match shows that <b>the build running is the build that was
    /// uploaded</b> — and nothing more than that. Both sides come from a file the creator
    /// controls, so this is not verification, not security, and not anti-cheat. It catches the
    /// honest case of an old build being run against a new upload.</para></summary>
    public static class PrototirBuildId
    {
        public const string FileName = "prototir-build.json";

        /// <summary>A fresh id per export, deliberately. Reusing one across builds would mean an
        /// older artifact still matched, which is precisely what this is meant to notice.</summary>
        public static string Write(string outputDirectory, string sdkVersion)
        {
            var id = Guid.NewGuid().ToString("N");
            var json = JsonUtility.ToJson(new Sidecar
            {
                buildId = id,
                sdk = "unity",
                sdkVersion = sdkVersion,
                createdAt = DateTime.UtcNow.ToString("o"),
            }, true);

            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, FileName), json);
                return id;
            }
            catch (Exception error)
            {
                // A build that cannot carry an id is still a perfectly good build: pairing simply
                // proceeds without one. Failing the export here would be wildly out of proportion.
                Debug.LogWarning($"Prototir: could not write {FileName} ({error.Message}). " +
                                 "The build will pair without a build id.");
                return null;
            }
        }

        [Serializable]
        private sealed class Sidecar
        {
            public string buildId;
            public string sdk;
            public string sdkVersion;
            public string createdAt;
        }
    }
}
