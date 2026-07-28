#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp;

/// <summary>Desk next[] peel — adapts habitat → DeskNextBuildUnit.</summary>
internal static partial class IdeCockpit
{
    static readonly DeskNextBuildUnit DeskNext = new();

    static object[] BuildNext(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        string? focusId,
        QualityGates.QualitySnap quality,
        IdeAlertChannel.Snap alert,
        IdeChkChannel.Snap? chk = null,
        IdeChkChannel.ProbeCtx? chkCtx = null)
    {
        string? qrhHot = null;
        string? qrhPulse = null;
        if (chkCtx is { } qCtx)
        {
            var qSuggest = IdeQrhChannel.SuggestFor(qCtx, chk);
            qrhHot = qSuggest.HotId;
            qrhPulse = qSuggest.Pulse;
        }

        var cards = DeskNext.Build(new DeskNextBuildUnit.Input(
            HasProject: session.ProjectRoot is not null,
            DeskBookmarkExists: File.Exists(DeskBookmark.FilePath),
            WorkIntentId: work.IntentId,
            WorkPulse: work.Pulse,
            AlertBeeping: alert.Level != IdeAlertChannel.Level.Clear,
            AlertPulse: alert.Pulse,
            AlertWhy: alert.Explain?.WhyLine,
            PressureArmed: IdePressureChannel.IsArmed(),
            PressurePulse: IdePressureChannel.PulseLine(),
            ChkOpenRequired: chk?.OpenRequired ?? 0,
            ChkPulse: chk?.Pulse,
            PhaseReviewOrVerify: session.Phase is CdpPhase.Review or CdpPhase.Verify,
            PhaseIsReview: session.Phase is CdpPhase.Review,
            QrhHotId: qrhHot,
            QrhPulse: qrhPulse,
            LayoutHint: alert.Sit?.LayoutHint,
            LayoutSeatNote: alert.Sit?.SeatNote,
            ProblemErrors: alert.ProblemErrors,
            AnyUndo: EditorComfort.AnyUndo(),
            AnyClipboard: EditorComfort.AnyClipboard(),
            AnyNavBack: EditorComfort.AnyNavBack(),
            QualityEnabled: quality.Enabled,
            QualityFail: quality.Fail,
            QualityWarn: quality.Warn,
            SuggestSniper: quality.SuggestSniper,
            SniperHasHold: EditSniper.HasHold,
            SniperPulse: EditSniper.PulseLine,
            ArchHasWork: IdeArchBoardChannel.HasActiveWork(session),
            ArchPulse: IdeArchBoardChannel.PulseLine(session),
            ToolchainPulse: IdeToolchainChannel.PulseLine(session),
            OnboardHasScan: IdeOnboardChannel.HasScan(session),
            OnboardPulse: IdeOnboardChannel.PulseLine(session),
            DiskChangedCount: buffer.DiskChangedCount,
            FocusId: focusId,
            BufferCount: buffer.Count,
            BufferDirtyCount: buffer.DirtyCount,
            GitDirty: gitRoot is { } g && GitIsDirty(g),
            TestFailed: test.Failed,
            DebugStopped: debug.Stopped,
            ShellRunning: shell.Running));

        return cards
            .Select(c => (object)new { id = c.Id, go = c.Go, label = c.Label, why = c.Why })
            .ToArray();
    }
}
