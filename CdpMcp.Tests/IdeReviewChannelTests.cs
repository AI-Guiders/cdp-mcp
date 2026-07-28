using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeReviewChannelTests
{
    [Fact]
    public void ListDirtyFiles_null_root_empty()
    {
        Assert.Empty(IdeReviewChannel.ListDirtyFiles(null));
    }

    [Fact]
    public void Board_pulse_mentions_review()
    {
        var session = new SessionContext { Phase = CdpPhase.Review, Object = CdpObjectKind.Code };
        var snap = IdeReviewChannel.Build(new IdeReviewChannel.Inputs(
            session, GitDirty: true, ProblemErrors: 0, TestsFailed: false, QualityFail: 0, QualityWarn: 0));
        Assert.Contains("review", snap.Pulse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ccl_bare_review_sets_go()
    {
        var applied = IdeRepl.Apply("review", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("review", go.GetString());
    }

    [Fact]
    public void Layout_review_maps_to_phase_review()
    {
        Assert.Equal("phase-review", IdePhaseLayout.LayoutIdFor(CdpPhase.Review));
    }

    [Fact]
    public void Ecl_review_active_on_phase()
    {
        var ctx = new IdeChkChannel.ProbeCtx(
            ProjectOpen: true,
            TaskOpen: true,
            GitKnown: true,
            GitDirty: true,
            TestsGreen: true,
            TestsFailed: false,
            ProblemsClean: true,
            DapStopped: false,
            DapActive: false,
            SniperOk: true,
            "review",
            null);
        var snap = IdeChkChannel.Build(ctx);
        Assert.Contains(snap.Active, r => r.Id == "review");
    }

    [Fact]
    public void ParsePhase_review_wire()
    {
        Assert.True(CdpEnumParse.TryParsePhase("review", out var p));
        Assert.Equal(CdpPhase.Review, p);
    }
}
