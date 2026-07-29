#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Plan-pulse fast path for BuildAsync — skip git / quality / full SoftOrgan glass / seat organ resolve
/// when go=plan + go_detail≠full + seats_detail≠full. Fixes cockpit hang on cmd=done|start|…
/// </summary>
internal static partial class IdeCockpit
{
    /// <summary>True when cockpit should return a slim plan-pulse desk instead of full BuildAsync spray.</summary>
    public static bool WantsPlanPulseFastPath(
        string? goVerb,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (goVerb is not { Length: > 0 })
            return false;
        if (CanonicalOrganPin(goVerb) is not "plan")
            return false;

        var detail = (OptString(args, "go_detail") ?? "pulse").Trim().ToLowerInvariant();
        if (detail is "full")
            return false;

        var seats = (OptString(args, "seats_detail") ?? OptString(args, "view_detail") ?? "").Trim()
            .ToLowerInvariant();
        if (seats is "full")
            return false;

        if (!string.IsNullOrWhiteSpace(OptString(args, "pane_full") ?? OptString(args, "full_pane")))
            return false;

        return true;
    }

    static bool AnyDeferredSoftWant(DeferredSoftWants w) =>
        w.Sys || w.Chk || w.Qrh || w.Alert || w.Problems || w.Plugins || w.Review;

    static DeskProbeBundle CollectPlanPulseProbeBundle(
        SessionContext session,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState)
    {
        var debug = CollectDebug(session);
        var test = CollectTest(session);
        var work = CollectWork(workspaceStore, workspaceState, session);
        var quality = new QualityGates.QualitySnap(
            Enabled: false, Warn: 0, Fail: 0, SuggestSniper: false, Pulse: "off");
        var problems = new IdeProblemsChannel.Snap(
            true, "problems · pulse", 0, 0, 0, 0, 0, Array.Empty<IdeProblemsChannel.Row>());
        var chkCtx = IdeChkChannel.CtxFrom(
            session,
            workspaceState.ActiveStageId is not null,
            !IdeIgniteArmHost.HasContinuityArms(),
            gitKnown: false,
            gitDirty: false,
            testsGreen: test is { Available: true, LastRun: not null, Success: true },
            testsFailed: test is { Available: true, LastRun: not null, Success: false },
            problemsClean: true,
            dapStopped: debug.Stopped,
            dapActive: debug.ActiveDap,
            sniperOk: true);
        var chkSnap = IdeChkChannel.Build(chkCtx);
        return new DeskProbeBundle(
            debug, test, work, quality, problems, chkCtx, chkSnap,
            GitDirty: false, TestsFailed: test is { Available: true, LastRun: not null, Success: false });
    }

    /// <summary>Plan glass only — no SoftOrgan board spray.</summary>
    static (IdeAlertChannel.Snap AlertSnap, IdeAlertChannel.Inputs AlertInputs) ApplyPlanPulseGlass(
        SessionContext session,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        BufferSnap buffer,
        ShellSnap shell,
        DeskProbeBundle probes)
    {
        IdeTaskManager.PublishGlass(workspaceStore, workspaceState, CdpEnumParse.ToWire(session.Phase));

        var alertInputs = new IdeAlertChannel.Inputs(
            probes.Quality,
            DiskChanged: buffer.DiskChangedCount,
            DapActive: probes.Debug.ActiveDap,
            DapStopped: probes.Debug.Stopped,
            ProblemErrors: 0,
            ProblemWarnings: 0,
            ShellRunning: shell.Running,
            ShellFailed: shell.Failed,
            GitDirty: false,
            Sit: null,
            StagePhaseMismatch: null,
            ChkOpenRequired: probes.ChkSnap.OpenRequired,
            ChkPulse: probes.ChkSnap.Pulse);
        var alertSnap = IdeAlertChannel.Build(alertInputs);
        return (alertSnap, alertInputs);
    }

    static string FinishPlanPulseDesk(
        SessionContext session,
        DocumentBufferStore docStore,
        ShellHabitat shellHabitat,
        InternetBrowserHabitat internetBrowser,
        IdeSettingsHabitat ideSettings,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        string mfd,
        string? focusId,
        object? goResult,
        object? warm)
    {
        var buffer = CollectBuffer(docStore.Scene());
        var shell = CollectShell(shellHabitat.Scene());
        var browser = internetBrowser.Pulse();
        var settingsPulse = ideSettings.Pulse();
        var probes = CollectPlanPulseProbeBundle(session, workspaceStore, workspaceState);
        var (alertSnap, _) = ApplyPlanPulseGlass(
            session, workspaceStore, workspaceState, buffer, shell, probes);
        goResult = SlimGoResult(goResult, OptString(args, "go_detail"));

        var (loci, next, focus, goVerbs) = BuildDeskNavigation(
            session, git: null, shell, browser, settingsPulse, buffer,
            probes.Debug, probes.Test, probes.Work, probes.Quality, focusId,
            alertSnap, probes.ChkSnap, probes.ChkCtx);

        if (IdeDeskSeats.IsSeatsMode())
        {
            return BuildPlanPulseSeatsSurface(
                session, args, mfd, focusId, goResult, warm, next, focus, alertSnap,
                loci, goVerbs);
        }

        return ComposeTilesSurface(
            session, mfd, tiles: null, pins: Array.Empty<string>(), goResult, warm, next, focus,
            alertSnap, loci, goVerbs, args, focusId);
    }

    /// <summary>Seat lines without ResolveSeatOrganPaneAsync — plan seat uses goResult pulse.</summary>
    static string BuildPlanPulseSeatsSurface(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string mfd,
        string? focusId,
        object? goResult,
        object? warm,
        object next,
        object? focus,
        IdeAlertChannel.Snap alertSnap,
        IReadOnlyList<Locus> loci,
        string[] goVerbs)
    {
        var seatMap = IdeDeskSeats.Snapshot();
        var seatPinList = IdeDeskSeats.Order.Select(s => seatMap[s]).ToArray();
        var seatPanes = new List<SeatPane>();
        foreach (var seatId in IdeDeskSeats.Order)
        {
            var organ = seatMap[seatId];
            if (organ is not { Length: > 0 })
            {
                seatPanes.Add(new SeatPane(seatId, null, true, false, true, "(empty)", null));
                continue;
            }

            var pin = CanonicalOrganPin(organ);
            if (pin is "plan" && goResult is not null)
            {
                var (ok, line) = IdeDeskView.LineFromPane(goResult, false, organ);
                seatPanes.Add(new SeatPane(seatId, organ, false, false, ok, line, null));
            }
            else
            {
                seatPanes.Add(new SeatPane(
                    seatId, organ, false, false, true, IdeDeskView.ShortOrgan(organ), null));
            }
        }

        return ComposeSeatsSurface(
            session, mfd, seatPanes, wantPanes: false, seatPinList,
            goResult, warm, next, focus, alertSnap, thrashNote: null,
            loci, goVerbs, args, focusId);
    }
}
