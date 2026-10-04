using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests
{
    /// <summary>The editor's update check: which tag is the newest release, and whether it is
    /// newer than the installed package.</summary>
    public class SdkVersionTests
    {
        [Fact]
        public void The_newest_release_tag_wins_and_previews_are_ignored()
        {
            Assert.Equal("0.10.0", PrototirSdkVersion.Latest(new[] { "v0.3.0", "v0.10.0", "v0.9.9", "v1.0.0-beta.1", "latest" }));
            Assert.Null(PrototirSdkVersion.Latest(new[] { "main", "v1", "v1.2.3.4" }));
        }

        [Fact]
        public void Only_a_readable_newer_release_counts_as_an_update()
        {
            Assert.True(PrototirSdkVersion.IsNewer("0.5.0", "0.4.0"));
            Assert.True(PrototirSdkVersion.IsNewer("v0.4.1", "0.4.0"));
            Assert.False(PrototirSdkVersion.IsNewer("0.4.0", "0.4.0"));
            Assert.False(PrototirSdkVersion.IsNewer("0.3.9", "0.4.0"));
            Assert.False(PrototirSdkVersion.IsNewer("0.5.0", "local"));
            Assert.False(PrototirSdkVersion.IsNewer("0.5.0-rc.1", "0.4.0"));
        }
    }
}
