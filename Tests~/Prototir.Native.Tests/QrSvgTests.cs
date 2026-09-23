using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests;

public class QrSvgTests
{
    [Fact]
    public void Decodes_the_server_renderer_horizontal_runs_and_quiet_zone()
    {
        // QRCoder emits one path with adjacent M...h...v1h-...z runs. The quiet zone is
        // encoded in the viewBox and background rect, so the first four modules stay white.
        const string svg = """
            <svg viewBox="0 0 21 21" width="84" height="84">
            <rect x="0" y="0" width="21" height="21" fill="#ffffff"/>
            <path fill="#111113" d="M4 4h7v1h-7zM15 4h2v1h-2zM4 5h1v1h-1z"/>
            </svg>
            """;

        Assert.True(PrototirQrSvg.TryDecode(svg, out var modules));
        Assert.Equal(21, modules.GetLength(0));
        Assert.False(modules[0, 0]);
        Assert.True(modules[4, 4]);
        Assert.True(modules[10, 4]);
        Assert.False(modules[11, 4]);
        Assert.True(modules[16, 4]);
        Assert.False(modules[5, 5]);
    }

    [Theory]
    [InlineData("<svg viewBox=\"0 0 21 21\"><path d=\"M4 4h99v1h-99z\"/></svg>")]
    [InlineData("<svg viewBox=\"0 0 21 21\"><path d=\"M4 4h7v2h-7z\"/></svg>")]
    [InlineData("<svg viewBox=\"0 0 21 22\"><path d=\"M4 4h7v1h-7z\"/></svg>")]
    public void Refuses_unexpected_shapes_instead_of_showing_a_corrupted_qr(string svg)
    {
        Assert.False(PrototirQrSvg.TryDecode(svg, out _));
    }
}
