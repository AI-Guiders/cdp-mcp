using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdePhaseLayoutTests
{
    [Theory]
    [InlineData(CdpPhase.Explore, "phase-explore")]
    [InlineData(CdpPhase.Plan, "agent")]
    [InlineData(CdpPhase.Act, "bug")]
    [InlineData(CdpPhase.Verify, "verify")]
    [InlineData(CdpPhase.Review, "phase-review")]
    [InlineData(CdpPhase.Handoff, "phase-handoff")]
    public void LayoutIdFor_maps_cycle(CdpPhase phase, string expected) =>
        Assert.Equal(expected, IdePhaseLayout.LayoutIdFor(phase));

    [Fact]
    public void SuggestLayout_still_flags_stale_plugins()
    {
        var seats = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = "plugins",
            ["forward"] = "editor_scene",
            ["m"] = "browser",
        };
        var (hint, _) = IdeAlertChannel.SuggestLayout(CdpPhase.Act, CdpObjectKind.Code, seats);
        Assert.Equal("agent", hint);
    }

    [Fact]
    public void Build_warns_on_stage_phase_mismatch()
    {
        var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            0, false, false,
            StagePhaseMismatch: "phase mismatch task@verify · session=act"));
        Assert.Equal(IdeAlertChannel.Level.Warn, snap.Level);
        Assert.Contains("phase mismatch", snap.Pulse, StringComparison.Ordinal);
    }
}
