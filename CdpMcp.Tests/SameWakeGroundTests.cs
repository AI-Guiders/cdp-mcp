#nullable enable
using Cdp.Core;
using CdpMcp.Cockpit.ComputingUnits;
using Xunit;

namespace CdpMcp.Tests;

public sealed class SameWakeGroundTests : IDisposable
{
    public SameWakeGroundTests()
    {
        IdeSameWakeLatch.Reset();
    }

    public void Dispose() => IdeSameWakeLatch.Reset();

    [Fact]
    public void Pulse_ok_then_buffer_mill_after_threshold()
    {
        dynamic ok = IdeGroundChannel.Handle(new SessionContext(), null);
        Assert.Equal("ok", (string)ok.pattern);

        for (var i = 0; i < IdeGroundChannel.BufferMillThreshold; i++)
            IdeSameWakeLatch.NoteBufferOp();

        dynamic mill = IdeGroundChannel.Handle(new SessionContext(), null);
        Assert.Equal("buffer_mill", (string)mill.pattern);
        Assert.Equal("inventory", (string)mill.next);
        Assert.True((bool)mill.resume_ok);
    }

    [Fact]
    public void Ignore_hint_nudge_once_then_null()
    {
        var first = IdeSameWakeLatch.TryConsumeIgnoreHintNudge(fullWake: true);
        Assert.NotNull(first);
        Assert.Contains("ignore_hint", first, StringComparison.Ordinal);

        var second = IdeSameWakeLatch.TryConsumeIgnoreHintNudge(fullWake: true);
        Assert.Null(second);

        IdeSameWakeLatch.NoteRecall();
        Assert.Null(IdeSameWakeLatch.TryConsumeIgnoreHintNudge(fullWake: true));
    }

    [Fact]
    public void DeskNext_buffer_mill_surfaces_ground_and_inventory()
    {
        var unit = new DeskNextBuildUnit();
        var cards = unit.Build(new DeskNextBuildUnit.Input(
            HasProject: true,
            DeskBookmarkExists: false,
            WorkIntentId: null,
            WorkPulse: null,
            AlertBeeping: false,
            AlertPulse: null,
            AlertWhy: null,
            PressureArmed: false,
            PressurePulse: null,
            PressureWhy: null,
            ChkOpenRequired: 0,
            ChkPulse: null,
            PhaseReviewOrVerify: false,
            PhaseIsReview: false,
            QrhHotId: null,
            QrhPulse: null,
            LayoutHint: null,
            LayoutSeatNote: null,
            ProblemErrors: 0,
            AnyUndo: false,
            AnyClipboard: false,
            AnyNavBack: false,
            QualityEnabled: false,
            QualityFail: 0,
            QualityWarn: 0,
            SuggestSniper: false,
            SniperHasHold: false,
            SniperArmed: false,
            SniperPulse: null,
            ArchHasWork: false,
            ArchPulse: null,
            ToolchainPulse: "",
            OnboardHasScan: true,
            OnboardPulse: null,
            DiskChangedCount: 0,
            FocusId: null,
            BufferCount: 0,
            BufferDirtyCount: 0,
            GitDirty: false,
            TestFailed: 0,
            DebugStopped: false,
            ShellRunning: 0,
            StampPending: false,
            BufferMillHot: true,
            GroundPulse: "ground · buffer_mill ×5"));

        Assert.Contains(cards, c => c.Go == "ground");
        Assert.Contains(cards, c => c.Go == "inventory");
    }

    [Fact]
    public void Ownership_postfix_mentions_equal_standing()
    {
        Assert.Contains("Equal standing", IdeIgniteChannel.ChargeOwnershipPostfix, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeskGoMap_resolves_ground()
    {
        var cat = new CdpMcp.Cockpit.Cds.DeskGoMapCatalog();
        Assert.True(cat.TryGet("ground", out var ground));
        Assert.Equal(IdeGroundChannel.ToolName, ground.Tool);
        Assert.True(cat.TryGet("cdp_ground", out var cdpGround));
        Assert.Equal(IdeGroundChannel.ToolName, cdpGround.Tool);
    }
}
