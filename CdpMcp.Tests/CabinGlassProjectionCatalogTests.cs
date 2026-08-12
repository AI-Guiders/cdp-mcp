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
    [InlineData("onboard_desk", "MarkdownPreview", "agent · M: onboard")]
    [InlineData("arch_desk", "SemanticMap", "agent · M: arch")]
    [InlineData("mcp", "AiChatSettings", "agent · M: mcp")]
    [InlineData("plan", null, "agent · P: plan")]
    [InlineData("ignite", "AiChatSettings", "agent · M: ignite")]
    [InlineData("alert", null, "agent · M: alert")]
    [InlineData("ecl", "MarkdownPreview", "agent · M: ecl · Face")]
    [InlineData("qrh", "MarkdownPreview", "agent · M: qrh · Face")]
    [InlineData("review", "Problems", "agent · M: review")]
    [InlineData("learn", "MarkdownPreview", "agent · M: learn")]
    [InlineData("webcam_desk", null, "agent · M: webcam")]
    [InlineData("find_desk", "FindDesk", "agent · M: find")]
    [InlineData("md_author", "MarkdownPreview", "agent · M: md_author")]
    [InlineData("report", "MarkdownPreview", "agent · M: report")]
    [InlineData("toolchain_desk", "Build", "agent · M: toolchain")]
    [InlineData("refactor", "RelatedFiles", "agent · M: refactor")]
    [InlineData("debt", "RelatedFiles", "agent · M: refactor")]
    [InlineData("project_switch", "SolutionExplorer", "agent · M: project_switch")]
    [InlineData("files_desk", "FilesDesk", "agent · M: files")]
    [InlineData("domain", "MarkdownPreview", "agent · M: domain")]
    [InlineData("ownership", "MarkdownPreview", "agent · M: domain")]
    [InlineData("rules", "MarkdownPreview", "agent · M: rules")]
    [InlineData("standing", "MarkdownPreview", "agent · M: rules")]
    [InlineData("cdp_rules", "MarkdownPreview", "agent · M: rules")]
    [InlineData("inventory", "MarkdownPreview", "agent · M: inventory")]
    [InlineData("gaps", "MarkdownPreview", "agent · M: inventory")]
    [InlineData("cdp_inventory", "MarkdownPreview", "agent · M: inventory")]
    [InlineData("verify_wave", "MarkdownPreview", "agent · M: verify_wave")]
    [InlineData("cdp_verify_wave", "MarkdownPreview", "agent · M: verify_wave")]
    [InlineData("calendar", null, "agent · M: calendar")]
    [InlineData("clock", null, "agent · M: calendar")]
    [InlineData("cdp_calendar", null, "agent · M: calendar")]
    [InlineData("fds", "FlightDataStorage", "agent · M: fds")]
    [InlineData("flight_data_storage", "FlightDataStorage", "agent · M: fds")]
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
    public void TryResolve_covers_every_SoftInstrumentKind_go_pin()
    {
        var meta = new SoftInstrumentBoardMetaCatalog();
        foreach (SoftInstrumentKind kind in Enum.GetValues<SoftInstrumentKind>())
        {
            var go = meta.Require(kind).Go;
            var proj = CabinGlassProjectionCatalog.TryResolve(go);
            Assert.True(
                proj is not null,
                $"SoftInstrumentKind.{kind} go='{go}' missing from CabinGlassProjectionCatalog (0-sync)");
            Assert.True(
                !string.IsNullOrWhiteSpace(proj!.Value.MfdPage)
                || !string.IsNullOrWhiteSpace(proj.Value.ChromeHint),
                $"SoftInstrumentKind.{kind} go='{go}' resolves empty projection");
        }
    }
}
