#nullable enable
using CdpMcp.Cockpit.ComputingUnits;
using Xunit;

namespace CdpMcp.Tests;

public sealed class ThrashJustCultureTests : IDisposable
{
    public ThrashJustCultureTests()
    {
        IdeThrashLatch.ResetForTests();
        CitizenFailStreakLedger.ResetForTests();
    }

    public void Dispose()
    {
        IdeThrashLatch.ResetForTests();
        CitizenFailStreakLedger.ResetForTests();
    }

    [Fact]
    public void ThrashWarnLines_is_350()
    {
        Assert.Equal(350, DocumentEditPlane.ThrashWarnLines);
        Assert.Equal(48_000, DocumentEditPlane.ThrashWarnChars);
    }

    [Fact]
    public void ThrashLatch_ring_counts_and_hot_pulse()
    {
        IdeThrashLatch.NoteSetTextLarge(@"D:\tmp\Big.cs");
        Assert.Equal(1, IdeThrashLatch.LargeSetTextCount(@"D:\tmp\Big.cs"));
        IdeThrashLatch.NoteSetTextLarge(@"D:\tmp\Big.cs");
        Assert.Equal(2, IdeThrashLatch.LargeSetTextCount(@"D:\tmp\Big.cs"));
        Assert.True(IdeThrashLatch.IsHot());
        Assert.Contains("set_text_large", IdeThrashLatch.PulseLine(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeskNext_thrash_hot_prefixes_edit_plan_and_scope()
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
            ToolchainPulse: "toolchain",
            OnboardHasScan: true,
            OnboardPulse: "onboard",
            DiskChangedCount: 0,
            FocusId: null,
            BufferCount: 0,
            BufferDirtyCount: 0,
            GitDirty: false,
            TestFailed: 0,
            DebugStopped: false,
            ShellRunning: 0,
            StampPending: false,
            ThrashHot: true,
            ThrashPulse: "thrash · set_text_large×2 · Big.cs"));
        Assert.True(cards.Length <= DeskNextBuildUnit.Cap);
        Assert.Contains(cards, c => c.Go == "edit_draft");
        Assert.Contains(cards, c => c.Go == "scope");
        var idx = Array.FindIndex(cards, c => c.Go == "edit_draft");
        Assert.True(idx is >= 0 and < 3);
    }

    [Fact]
    public void Qrh_set_text_thrash_page_exists()
    {
        Assert.Contains(IdeQrhChannel.Builtins(), p => p.Id == "set-text-thrash");
    }

    [Fact]
    public void FailStreak_take_soft_refuses_after_threshold()
    {
        var path = @"D:\missing\Invent.cs";
        for (var i = 0; i < CitizenFailStreakLedger.DefaultThreshold; i++)
            CitizenFailStreakLedger.NoteFailure("take", path);

        var route = new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.Take, "@intent take path=" + path, Ok: true, Op: "take", Path: path);
        var refuse = CitizenFailStreakLedger.TryRefuseTake(route);
        Assert.NotNull(refuse);
        Assert.False(refuse!.Ok);
        Assert.Contains(CitizenFailStreakLedger.RefuseFailStreakTake, refuse.Reason, StringComparison.Ordinal);
    }
}
