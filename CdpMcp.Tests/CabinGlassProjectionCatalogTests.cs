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
    [InlineData("correspondence", "Correspondence", null)]
    [InlineData("hybrid_index", "HybridIndex", null)]
    [InlineData("options", "AiChatSettings", null)]
    [InlineData("pressure_desk", null, "agent · M: pressure")]
    [InlineData("onboard_desk", null, "agent · M: onboard")]
    [InlineData("arch_desk", null, "agent · M: arch")]
    [InlineData("mcp", null, "agent · M: mcp")]
    [InlineData("plan", null, "agent · P: plan")]
    [InlineData("ignite", "AiChatSettings", "agent · M: ignite")]
    [InlineData("alert", null, "agent · M: alert")]
    [InlineData("ecl", null, "agent · M: ecl")]
    [InlineData("qrh", null, "agent · M: qrh")]
    [InlineData("learn", null, "agent · M: learn")]
    [InlineData("webcam_desk", null, "agent · M: webcam")]
    [InlineData("find_desk", "RelatedFiles", "agent · M: find")]
    [InlineData("md_author", "MarkdownPreview", "agent · M: md_author")]
    [InlineData("project_switch", null, "agent · M: project_switch")]
    [InlineData("domain", null, "agent · M: domain")]
    [InlineData("ownership", null, "agent · M: domain")]
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

    [Fact]
    public void TryResolve_covers_every_SoftOrganKind_go_pin()
    {
        var meta = new SoftOrganBoardMetaCatalog();
        foreach (SoftOrganKind kind in Enum.GetValues<SoftOrganKind>())
        {
            var go = meta.Require(kind).Go;
            var proj = CabinGlassProjectionCatalog.TryResolve(go);
            Assert.True(
                proj is not null,
                $"SoftOrganKind.{kind} go='{go}' missing from CabinGlassProjectionCatalog (0-sync)");
            Assert.True(
                !string.IsNullOrWhiteSpace(proj!.Value.MfdPage)
                || !string.IsNullOrWhiteSpace(proj.Value.ChromeHint),
                $"SoftOrganKind.{kind} go='{go}' resolves empty projection");
        }
    }
}
