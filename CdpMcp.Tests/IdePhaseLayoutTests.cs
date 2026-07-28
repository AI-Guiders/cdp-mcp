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
    public void Build_stage_phase_mismatch_is_advisory_not_warn()
    {
        var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            0, false, false,
            StagePhaseMismatch: "phase mismatch task@verify · session=act"));
        Assert.Equal(IdeAlertChannel.Level.Clear, snap.Level);
        Assert.Contains("clear", snap.Pulse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(snap.Lines, l => l.Contains("phase mismatch", StringComparison.Ordinal));
        Assert.Null(snap.Explain);
    }
}
