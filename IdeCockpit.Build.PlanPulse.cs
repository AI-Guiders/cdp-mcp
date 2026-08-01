#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Desk-pulse fast path for BuildAsync — skip upfront git / quality / full SoftOrgan seat resolve.
/// <c>go_detail=full</c> = organ depth only. <c>seats_detail=full</c> alone stays on pulse
/// (W-spray refused early — same as SeatsDetailGateUnit). <c>pane_full=</c> stays on pulse and
/// resolves one matched seat only. CDP-ADR-0020: deferred soft organs skip glass spray; organ-only skip nav.
/// Plan stays a special-case of this path.
/// </summary>
internal static partial class IdeCockpit
{
    /// <summary>True when cockpit should return a slim desk-pulse instead of full BuildAsync spray.</summary>
    /// <summary>True when cockpit should return a slim desk-pulse instead of full BuildAsync spray.</summary>
    public static bool WantsDeskPulseFastPath(IReadOnlyDictionary<string, JsonElement> args)
    {
        // Desk stays pulse always (ADR-0020). go_detail=full = organ depth; seats_detail=full alone
        // thrash-refuses; pane_full= resolves one seat on pulse (not TryGitAsync / all-seat spray).
        _ = args;
        return true;
    }

    /// <summary>Plan-only gate (tests + legacy). Desk pulse + organ pin is plan.</summary>
    public static bool WantsPlanPulseFastPath(
        string? goVerb,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (goVerb is not { Length: > 0 })
            return false;
        if (CanonicalOrganPin(goVerb) is not "plan")
            return false;
        return WantsDeskPulseFastPath(args);
    }

    static bool AnyDeferredSoftWant(DeferredSoftWants w) =>
        w.Sys || w.Chk || w.Qrh || w.Alert || w.Problems || w.Plugins || w.Review;

    /// <summary>Thrash when seats_detail=full without pane_full (early refuse on pulse path).</summary>
    static string? DeskPulseWSprayThrash(IReadOnlyDictionary<string, JsonElement> args)
    {
        var gate = SeatsDetailGate.Compute(new SeatsDetailGateUnit.Input(
            SeatsDetailRaw: OptString(args, "seats_detail") ?? OptString(args, "view_detail"),
            FullPane: OptString(args, "pane_full") ?? OptString(args, "full_pane"),
            SeatsPanesFlag: BoolOr(args, "seats_panes", false),
            CompactDefaultTrue: BoolOr(args, "compact", true)));
        return gate.ThrashNote;
    }

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

    /// <summary>Plan soft-dispatch already filled goResult — sync slim desk.</summary>
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
            return BuildDeskPulseSeatsSurface(
                session, args, mfd, focusId, goResult, warm, next, focus, alertSnap,
                loci, goVerbs, resultPin: "plan");
        }

        return ComposeTilesSurface(
            session, mfd, tiles: null, pins: Array.Empty<string>(), goResult, warm, next, focus,
            alertSnap, loci, goVerbs, args, focusId);
    }

    /// <summary>
    /// Organ-only pulse: deferred soft want, no residual goVerb — board + slim go, skip nav.
    /// </summary>
    static string FinishOrganPulseDesk(
        SessionContext session,
        DocumentBufferStore docStore,
        ShellHabitat shellHabitat,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        string mfd,
        string? focusId,
        object? goResult,
        object? warm,
        DeferredSoftWants deferred)
    {
        var buffer = CollectBuffer(docStore.Scene());
        var shell = CollectShell(shellHabitat.Scene());
        var probes = CollectPlanPulseProbeBundle(session, workspaceStore, workspaceState);
        (goResult, var alertSnap, _) = ApplyDeferredSoftOrgans(
            deferred, goResult, session, docStore, workspaceStore, workspaceState, args,
            git: null, shell, buffer, probes.Debug, probes.Test, probes.Work, probes.Quality,
            probes.Problems, probes.ChkCtx, probes.ChkSnap,
            publishGlassSpray: false);
        goResult = SlimGoResult(goResult, OptString(args, "go_detail"));
        var resultPin = TryGoPinFromResult(goResult);
        object next = Array.Empty<object>();

        if (IdeDeskSeats.IsSeatsMode())
        {
            return BuildDeskPulseSeatsSurface(
                session, args, mfd, focusId, goResult, warm, next, focus: null, alertSnap,
                loci: Array.Empty<Locus>(), goVerbs: Array.Empty<string>(), resultPin);
        }

        return ComposeTilesSurface(
            session, mfd, tiles: null, pins: Array.Empty<string>(), goResult, warm, next, focus: null,
            alertSnap, loci: Array.Empty<Locus>(), goVerbs: Array.Empty<string>(), args, focusId);
    }

    /// <summary>
    /// Desk pulse for bare / editor / git / leftover go — no upfront git spray, no ResolveSeatOrganPaneAsync.
    /// Editor uses local snap (not cdp_editor_scene dispatch); git loads only when go is git.
    /// Deferred soft organs (alert/chk/…) apply on cheap PlanPulse probes — not full CollectProbeBundle.
    /// CDP-ADR-0020: deferred apply skips multi-channel glass spray on this path.
    /// </summary>
    static string? TryGoPinFromResult(object? goResult)
    {
        if (goResult is null)
            return null;
        if (goResult is JsonElement je
            && je.ValueKind == JsonValueKind.Object
            && je.TryGetProperty("go", out var g)
            && g.ValueKind == JsonValueKind.String)
            return CanonicalOrganPin(g.GetString() ?? "");

        var prop = goResult.GetType().GetProperty("go");
        if (prop?.GetValue(goResult) is string s && s.Length > 0)
            return CanonicalOrganPin(s);
        return null;
    }

    /// <summary>Seat lines without ResolveSeatOrganPaneAsync — matched go seat uses goResult pulse.</summary>
    static string BuildDeskPulseSeatsSurface(
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
        string[] goVerbs,
        string? resultPin)
    {
        var seatMap = IdeDeskSeats.Snapshot();
        var seatPinList = IdeDeskSeats.Order.Select(s => seatMap[s]).ToArray();
        var seatPanes = new List<SeatPane>();
        var wantPin = resultPin is { Length: > 0 } ? CanonicalOrganPin(resultPin) : null;
        foreach (var seatId in IdeDeskSeats.Order)
        {
            var organ = seatMap[seatId];
            if (organ is not { Length: > 0 })
            {
                seatPanes.Add(new SeatPane(seatId, null, true, false, true, "(empty)", null));
                continue;
            }

            var pin = CanonicalOrganPin(organ);
            if (goResult is not null
                && wantPin is { Length: > 0 }
                && pin == wantPin)
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
            goResult, warm, next, focus, alertSnap, DeskPulseWSprayThrash(args),
            loci, goVerbs, args, focusId);
    }
}