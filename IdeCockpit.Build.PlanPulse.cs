#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Desk-pulse fast path for BuildAsync — skip upfront git / quality / full SoftOrgan seat resolve
/// when seats_detail≠full + no pane_full. <c>go_detail=full</c> only expands <c>go.result</c>
/// (DispatchGo); it must NOT force the slow desk spray (hung agents ~minutes on soft organs).
/// Plan stays a special-case of this path.
/// </summary>
internal static partial class IdeCockpit
{
    /// <summary>True when cockpit should return a slim desk-pulse instead of full BuildAsync spray.</summary>
    public static bool WantsDeskPulseFastPath(IReadOnlyDictionary<string, JsonElement> args)
    {
        // Intentionally ignore go_detail=full — that is organ-dump depth, not desk spray.
        var seats = (OptString(args, "seats_detail") ?? OptString(args, "view_detail") ?? "").Trim()
            .ToLowerInvariant();
        if (seats is "full")
            return false;

        if (!string.IsNullOrWhiteSpace(OptString(args, "pane_full") ?? OptString(args, "full_pane")))
            return false;

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
    /// Desk pulse for bare / editor / git / leftover go — no upfront git spray, no ResolveSeatOrganPaneAsync.
    /// Editor uses local snap (not cdp_editor_scene dispatch); git loads only when go is git.
    /// Deferred soft organs (alert/chk/…) apply on cheap PlanPulse probes — not full CollectProbeBundle.
    /// CDP-ADR-0020: deferred apply skips multi-channel glass spray on this path.
    /// </summary>
    static async Task<string> FinishDeskPulseAsync(
        SessionContext session,
        DocumentBufferStore docStore,
        ShellHabitat shellHabitat,
        InternetBrowserHabitat internetBrowser,
        IdeSettingsHabitat ideSettings,
        McpOutletHabitat mcpOutlet,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        string mfd,
        string? focusId,
        string? goVerb,
        object? goResult,
        bool includeSubmodules,
        object? warm,
        DeferredSoftWants deferred,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var buffer = CollectBuffer(docStore.Scene());
        var shell = CollectShell(shellHabitat.Scene());
        var browser = internetBrowser.Pulse();
        var mcpPulse = mcpOutlet.Pulse();
        var settingsPulse = ideSettings.Pulse();
        JsonElement? git = null;
        string? resultPin = TryGoPinFromResult(goResult);

        if (goVerb is { Length: > 0 })
        {
            var rawPin = ResolvePinName(goVerb.Trim()) ?? goVerb.Trim();
            var pin = CanonicalOrganPin(rawPin);
            resultPin = pin;

            if (pin is "editor_scene")
            {
                goResult = EditorSnapPane(buffer);
                if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(rawPin))
                    IdeDeskSeats.PlaceOrgan(rawPin);
            }
            else if (pin is "git_scene")
            {
                git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken)
                    .ConfigureAwait(false);
                goResult = WorldSnapPane(pin, git, shell, browser, mcpPulse);
                if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(rawPin))
                    IdeDeskSeats.PlaceOrgan(rawPin);
            }
            else
            {
                (goResult, _, git, shell, browser, mcpPulse) = await ApplyWorldOrGoAsync(
                    goVerb, goResult, args, buffer, focusId, session, byDomain, includeSubmodules,
                    shellHabitat, internetBrowser, mcpOutlet, git, shell, browser, mcpPulse,
                    dispatch, cancellationToken).ConfigureAwait(false);
                resultPin = TryGoPinFromResult(goResult) ?? pin;
            }
        }

        var probes = CollectPlanPulseProbeBundle(session, workspaceStore, workspaceState);
        IdeAlertChannel.Snap alertSnap;
        if (AnyDeferredSoftWant(deferred))
        {
            (goResult, alertSnap, _) = ApplyDeferredSoftOrgans(
                deferred, goResult, session, docStore, workspaceStore, workspaceState, args,
                git, shell, buffer, probes.Debug, probes.Test, probes.Work, probes.Quality,
                probes.Problems, probes.ChkCtx, probes.ChkSnap,
                publishGlassSpray: false);
            resultPin = TryGoPinFromResult(goResult) ?? resultPin;
        }
        else
        {
            (alertSnap, _) = ApplyPlanPulseGlass(
                session, workspaceStore, workspaceState, buffer, shell, probes);
        }

        goResult = SlimGoResult(goResult, OptString(args, "go_detail"));

        var (loci, next, focus, goVerbs) = BuildDeskNavigation(
            session, git, shell, browser, settingsPulse, buffer,
            probes.Debug, probes.Test, probes.Work, probes.Quality, focusId,
            alertSnap, probes.ChkSnap, probes.ChkCtx);

        if (IdeDeskSeats.IsSeatsMode())
        {
            return BuildDeskPulseSeatsSurface(
                session, args, mfd, focusId, goResult, warm, next, focus, alertSnap,
                loci, goVerbs, resultPin);
        }

        return ComposeTilesSurface(
            session, mfd, tiles: null, pins: Array.Empty<string>(), goResult, warm, next, focus,
            alertSnap, loci, goVerbs, args, focusId);
    }

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
            goResult, warm, next, focus, alertSnap, thrashNote: null,
            loci, goVerbs, args, focusId);
    }
}
