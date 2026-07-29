using CdpMcp.Cockpit.Surface;
using Xunit;

namespace CdpMcp.Tests;

public class CabinGlassProjectionCatalogTests
{
    [Theory]
    [InlineData("shell", "Terminal", null)]
    [InlineData("shell_scene", "Terminal", null)]
    [InlineData("quality", "Problems", null)]
    [InlineData("gates", "Problems", null)]
    [InlineData("browser", "WebAiPortal", null)]
    [InlineData("internet_browser", "WebAiPortal", null)]
    [InlineData("build_desk", "Build", null)]
    [InlineData("pressure_desk", null, "agent · M: pressure")]
    [InlineData("ignite", "AiChatSettings", "agent · M: ignite")]
    public void TryResolve_maps_gap_organs(string pin, string? mfd, string? chrome)
    {
        var proj = CabinGlassProjectionCatalog.TryResolve(pin);
        Assert.NotNull(proj);
        Assert.Equal(mfd, proj!.Value.MfdPage);
        Assert.Equal(chrome, proj.Value.ChromeHint);
    }

    [Fact]
    public void TryResolve_unknown_returns_null()
    {
        Assert.Null(CabinGlassProjectionCatalog.TryResolve("nope_organ"));
        Assert.Null(CabinGlassProjectionCatalog.TryResolve(null));
        Assert.Null(CabinGlassProjectionCatalog.TryResolve(" "));
    }
}
