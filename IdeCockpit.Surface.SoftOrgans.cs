#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Soft-organ seat pane resolve — peeled from BuildSeatsDeskSurfaceAsync loop.</summary>
internal static partial class IdeCockpit
{
    static readonly SoftOrganAliasCatalog SoftOrganAliases = new();
    static readonly SoftOrganBoardMetaCatalog SoftOrganMeta = new();

    static async Task<object> ResolveSeatOrganPaneAsync(
        string organ,
        string planPin,
        bool wantFull,
        Dictionary<string, JsonElement> tileArgs,
        SessionContext session,
        DocumentBufferStore docStore,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IdeAlertChannel.Inputs alertInputs,
        JsonElement? git,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        McpOutletHabitat.McpPulse mcpPulse,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        QualityGates.QualitySnap quality,
        IdeProblemsChannel.Snap problems,
        IdeChkChannel.ProbeCtx chkCtx,
        IdeChkChannel.Snap chkSnap,
        bool gitDirty,
        bool testsFailed,
        string? focusId,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        if (SoftOrganAliases.TryResolve(planPin) is SoftOrganKind kind)
        {
            return kind switch
            {
                SoftOrganKind.Plan => ResolvePlanPane(wantFull, tileArgs, session, workspaceStore, workspaceState),
                SoftOrganKind.Report => PresentFullOr(
                    kind, IdeReportBoard.Handle(session, tileArgs), wantFull),
                SoftOrganKind.FindDesk => PresentFullOr(
                    kind, IdeFindChannel.Handle(docStore, session, tileArgs), wantFull),
                SoftOrganKind.SaDesk => PresentFullOr(
                    kind, IdeSaChannel.Handle(docStore, session, tileArgs), wantFull),
                SoftOrganKind.DebugDesk => PresentFullOr(
                    kind, IdeDebugSaChannel.Handle(session, tileArgs), wantFull),
                SoftOrganKind.TestDesk => PresentFullOr(
                    kind, IdeTestSaChannel.Handle(session, tileArgs), wantFull),
                SoftOrganKind.BuildDesk => PresentFullOr(
                    kind, IdeBuildSaChannel.Handle(session, tileArgs), wantFull),
                SoftOrganKind.Crm => PresentFullOr(
                    kind, IdeCrmChannel.Handle(session, workspaceStore, workspaceState, tileArgs), wantFull),
                SoftOrganKind.FilesDesk => PresentFullOr(
                    kind, IdeFilesChannel.Handle(docStore, session, tileArgs), wantFull),
                SoftOrganKind.IgniteDesk => PresentFullOr(
                    kind, IdeIgniteChannel.Handle(tileArgs), wantFull),
                SoftOrganKind.WebcamDesk => PresentFullOr(
                    kind, IdeWebcamChannel.Handle(session, tileArgs), wantFull),
                SoftOrganKind.PressureDesk => ResolvePressurePane(wantFull, session, tileArgs),
                SoftOrganKind.OnboardDesk => ResolveOnboardPane(wantFull, session, tileArgs),
                SoftOrganKind.Toolchain => ResolveToolchainPane(wantFull, session, tileArgs),
                SoftOrganKind.Alert => PresentFullOr(
                    kind, IdeAlertChannel.Handle(alertInputs, tileArgs), wantFull),
                SoftOrganKind.Problems => ResolveProblemsPane(wantFull, docStore, session, tileArgs),
                SoftOrganKind.Plugins => ResolvePluginsPane(wantFull, docStore, session, tileArgs),
                SoftOrganKind.Quality => ResolveQualityPane(wantFull, docStore, session),
                SoftOrganKind.Sys => PresentFullOr(
                    kind, BuildSysOrgan(session, git, shell, buffer, debug, test, work), wantFull),
                SoftOrganKind.Ecl => PresentFullOr(
                    kind, IdeChkChannel.Handle(chkCtx, tileArgs), wantFull),
                SoftOrganKind.Qrh => PresentFullOr(
                    kind, IdeQrhChannel.Handle(chkCtx, tileArgs, chkSnap), wantFull),
                SoftOrganKind.Review => ResolveReviewPane(
                    wantFull, session, tileArgs, gitDirty, problems, testsFailed, quality, chkSnap),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        if (!wantFull && IdeWorldChannel.IsWorldOrgan(planPin))
            return WorldSnapPane(planPin, git, shell, browser, mcpPulse);

        if (!wantFull && planPin is ("editor_scene" or "buffer_scene" or "editor" or "buffer"))
            return EditorSnapPane(buffer);

        if (!wantFull && planPin is ("script_scene" or "script" or "probe"))
        {
            var (sok, sp) = ScriptScene.Pulse(session);
            return new { ok = sok, go = "script_scene", detail = "pulse", pulse = sp, snap = true };
        }

        return await DispatchGoAsync(organ, tileArgs, buffer, focusId, dispatch, cancellationToken)
            .ConfigureAwait(false);
    }

    static object PresentFullOr(SoftOrganKind kind, object board, bool wantFull)
    {
        var m = SoftOrganMeta.Require(kind);
        return SeatOrganPanePresenter.FullOr(board, wantFull, m.Go, m.Tool);
    }

    static object ResolvePlanPane(
        bool wantFull,
        Dictionary<string, JsonElement> tileArgs,
        SessionContext session,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState)
    {
        if (workspaceStore is null)
        {
            return new
            {
                ok = false,
                go = "plan",
                error = "no_workspace",
                hint = "Intent workspace WitDB unavailable."
            };
        }

        tileArgs["session_phase"] = JsonSerializer.SerializeToElement(
            CdpEnumParse.ToWire(session.Phase));
        var board = IdeTaskManager.Handle(workspaceStore, workspaceState, tileArgs);
        return PresentFullOr(SoftOrganKind.Plan, board, wantFull);
    }

    static object ResolvePressurePane(
        bool wantFull, SessionContext session, Dictionary<string, JsonElement> tileArgs)
    {
        var m = SoftOrganMeta.Require(SoftOrganKind.PressureDesk);
        var board = IdePressureChannel.Handle(session, tileArgs);
        return SeatOrganPanePresenter.PulseOrFull(
            wantFull, board, m.Go, m.Tool, IdePressureChannel.PulseLine(),
            IdePressureChannel.SchemaVersion, "pane_full= / go_detail=full for checklist dump");
    }

    static object ResolveOnboardPane(
        bool wantFull, SessionContext session, Dictionary<string, JsonElement> tileArgs)
    {
        var m = SoftOrganMeta.Require(SoftOrganKind.OnboardDesk);
        return SeatOrganPanePresenter.PulseOrFull(
            wantFull,
            IdeOnboardChannel.Handle(session, tileArgs),
            m.Go, m.Tool,
            IdeOnboardChannel.PulseLine(session),
            IdeOnboardChannel.SchemaVersion,
            "pane_full= / go_detail=full · op=scan to refresh");
    }

    static object ResolveToolchainPane(
        bool wantFull, SessionContext session, Dictionary<string, JsonElement> tileArgs)
    {
        var m = SoftOrganMeta.Require(SoftOrganKind.Toolchain);
        return SeatOrganPanePresenter.PulseOrFull(
            wantFull,
            IdeToolchainChannel.Handle(session, tileArgs),
            m.Go, m.Tool,
            IdeToolchainChannel.PulseLine(session),
            IdeToolchainChannel.SchemaVersion,
            "pane_full= / go_detail=full · op=ensure id=python|gcc|…");
    }

    static object ResolveProblemsPane(
        bool wantFull,
        DocumentBufferStore docStore,
        SessionContext session,
        Dictionary<string, JsonElement> tileArgs)
    {
        var m = SoftOrganMeta.Require(SoftOrganKind.Problems);
        var board = IdeProblemsChannel.Handle(docStore, session, tileArgs);
        return wantFull
            ? SeatOrganPanePresenter.Full(board, m.Go, m.Tool)
            : SeatOrganPanePresenter.PulseWithResult(
                board, m.Go, IdeProblemsChannel.Build(docStore, session).Pulse, m.Tool);
    }

    static object ResolvePluginsPane(
        bool wantFull,
        DocumentBufferStore docStore,
        SessionContext session,
        Dictionary<string, JsonElement> tileArgs)
    {
        var m = SoftOrganMeta.Require(SoftOrganKind.Plugins);
        var board = IdePluginsChannel.Handle(docStore, session, tileArgs);
        return wantFull
            ? SeatOrganPanePresenter.Full(board, m.Go, m.Tool)
            : SeatOrganPanePresenter.PulseWithResult(
                board, m.Go, IdePluginsChannel.Build().Pulse, m.Tool);
    }

    static object ResolveQualityPane(
        bool wantFull, DocumentBufferStore docStore, SessionContext session)
    {
        var m = SoftOrganMeta.Require(SoftOrganKind.Quality);
        var q = QualityGates.EvaluateStore(docStore, session.ProjectRoot);
        return wantFull
            ? SeatOrganPanePresenter.Full(q, m.Go, m.Tool)
            : SeatOrganPanePresenter.PulseWithResult(
                q, m.Go, QualityGates.Snap(docStore, session.ProjectRoot).Pulse, m.Tool);
    }

    static object ResolveReviewPane(
        bool wantFull,
        SessionContext session,
        Dictionary<string, JsonElement> tileArgs,
        bool gitDirty,
        IdeProblemsChannel.Snap problems,
        bool testsFailed,
        QualityGates.QualitySnap quality,
        IdeChkChannel.Snap chkSnap)
    {
        var reviewInputs = new IdeReviewChannel.Inputs(
            session, gitDirty, problems.Errors, testsFailed, quality.Fail, quality.Warn, chkSnap);
        return PresentFullOr(
            SoftOrganKind.Review, IdeReviewChannel.Handle(reviewInputs, tileArgs), wantFull);
    }
}
