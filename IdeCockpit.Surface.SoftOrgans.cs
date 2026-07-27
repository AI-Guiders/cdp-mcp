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
                SoftOrganKind.Report => SeatOrganPanePresenter.FullOr(
                    IdeReportBoard.Handle(session, tileArgs), wantFull, "report", "report_board"),
                SoftOrganKind.FindDesk => SeatOrganPanePresenter.FullOr(
                    IdeFindChannel.Handle(docStore, session, tileArgs), wantFull, "find_desk", IdeFindChannel.ToolName),
                SoftOrganKind.SaDesk => SeatOrganPanePresenter.FullOr(
                    IdeSaChannel.Handle(docStore, session, tileArgs), wantFull, "sa_desk", IdeSaChannel.ToolName),
                SoftOrganKind.DebugDesk => SeatOrganPanePresenter.FullOr(
                    IdeDebugSaChannel.Handle(session, tileArgs), wantFull, "debug_desk", IdeDebugSaChannel.ToolName),
                SoftOrganKind.TestDesk => SeatOrganPanePresenter.FullOr(
                    IdeTestSaChannel.Handle(session, tileArgs), wantFull, "test_desk", IdeTestSaChannel.ToolName),
                SoftOrganKind.BuildDesk => SeatOrganPanePresenter.FullOr(
                    IdeBuildSaChannel.Handle(session, tileArgs), wantFull, "build_desk", IdeBuildSaChannel.ToolName),
                SoftOrganKind.Crm => SeatOrganPanePresenter.FullOr(
                    IdeCrmChannel.Handle(session, workspaceStore, workspaceState, tileArgs),
                    wantFull, "crm", IdeCrmChannel.ToolName),
                SoftOrganKind.FilesDesk => SeatOrganPanePresenter.FullOr(
                    IdeFilesChannel.Handle(docStore, session, tileArgs), wantFull, "files_desk", IdeFilesChannel.ToolName),
                SoftOrganKind.IgniteDesk => SeatOrganPanePresenter.FullOr(
                    IdeIgniteChannel.Handle(tileArgs), wantFull, "ignite_desk", IdeIgniteChannel.ToolName),
                SoftOrganKind.WebcamDesk => SeatOrganPanePresenter.FullOr(
                    IdeWebcamChannel.Handle(session, tileArgs), wantFull, "webcam_desk", IdeWebcamChannel.ToolName),
                SoftOrganKind.PressureDesk => ResolvePressurePane(wantFull, session, tileArgs),
                SoftOrganKind.OnboardDesk => ResolveOnboardPane(wantFull, session, tileArgs),
                SoftOrganKind.Toolchain => ResolveToolchainPane(wantFull, session, tileArgs),
                SoftOrganKind.Alert => SeatOrganPanePresenter.FullOr(
                    IdeAlertChannel.Handle(alertInputs, tileArgs), wantFull, "alert", "alert_channel"),
                SoftOrganKind.Problems => ResolveProblemsPane(wantFull, docStore, session, tileArgs),
                SoftOrganKind.Plugins => ResolvePluginsPane(wantFull, docStore, session, tileArgs),
                SoftOrganKind.Quality => ResolveQualityPane(wantFull, docStore, session),
                SoftOrganKind.Sys => SeatOrganPanePresenter.FullOr(
                    BuildSysOrgan(session, git, shell, buffer, debug, test, work), wantFull, "sys", "sys_organ"),
                SoftOrganKind.Ecl => SeatOrganPanePresenter.FullOr(
                    IdeChkChannel.Handle(chkCtx, tileArgs), wantFull, "ecl", "ecl_organ"),
                SoftOrganKind.Qrh => SeatOrganPanePresenter.FullOr(
                    IdeQrhChannel.Handle(chkCtx, tileArgs, chkSnap), wantFull, "qrh", "qrh_organ"),
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
        return SeatOrganPanePresenter.FullOr(board, wantFull, "plan", "cdp_work");
    }

    static object ResolvePressurePane(
        bool wantFull, SessionContext session, Dictionary<string, JsonElement> tileArgs)
    {
        var board = IdePressureChannel.Handle(session, tileArgs);
        return wantFull
            ? SeatOrganPanePresenter.Full(board, "pressure_desk", IdePressureChannel.ToolName)
            : SeatOrganPanePresenter.Pulse(
                "pressure_desk", IdePressureChannel.ToolName, IdePressureChannel.PulseLine(),
                IdePressureChannel.SchemaVersion, "pane_full= / go_detail=full for checklist dump");
    }

    static object ResolveOnboardPane(
        bool wantFull, SessionContext session, Dictionary<string, JsonElement> tileArgs) =>
        wantFull
            ? SeatOrganPanePresenter.Full(
                IdeOnboardChannel.Handle(session, tileArgs), "onboard_desk", IdeOnboardChannel.ToolName)
            : SeatOrganPanePresenter.Pulse(
                "onboard_desk", IdeOnboardChannel.ToolName, IdeOnboardChannel.PulseLine(session),
                IdeOnboardChannel.SchemaVersion, "pane_full= / go_detail=full · op=scan to refresh");

    static object ResolveToolchainPane(
        bool wantFull, SessionContext session, Dictionary<string, JsonElement> tileArgs) =>
        wantFull
            ? SeatOrganPanePresenter.Full(
                IdeToolchainChannel.Handle(session, tileArgs), "toolchain", IdeToolchainChannel.ToolName)
            : SeatOrganPanePresenter.Pulse(
                "toolchain", IdeToolchainChannel.ToolName, IdeToolchainChannel.PulseLine(session),
                IdeToolchainChannel.SchemaVersion, "pane_full= / go_detail=full · op=ensure id=python|gcc|…");

    static object ResolveProblemsPane(
        bool wantFull,
        DocumentBufferStore docStore,
        SessionContext session,
        Dictionary<string, JsonElement> tileArgs)
    {
        var board = IdeProblemsChannel.Handle(docStore, session, tileArgs);
        return wantFull
            ? SeatOrganPanePresenter.Full(board, "problems", "problems_channel")
            : SeatOrganPanePresenter.PulseWithResult(
                board, "problems", IdeProblemsChannel.Build(docStore, session).Pulse);
    }

    static object ResolvePluginsPane(
        bool wantFull,
        DocumentBufferStore docStore,
        SessionContext session,
        Dictionary<string, JsonElement> tileArgs)
    {
        var board = IdePluginsChannel.Handle(docStore, session, tileArgs);
        return wantFull
            ? SeatOrganPanePresenter.Full(board, "plugins", "plugins_channel")
            : SeatOrganPanePresenter.PulseWithResult(
                board, "plugins", IdePluginsChannel.Build().Pulse);
    }

    static object ResolveQualityPane(
        bool wantFull, DocumentBufferStore docStore, SessionContext session)
    {
        var q = QualityGates.EvaluateStore(docStore, session.ProjectRoot);
        return wantFull
            ? SeatOrganPanePresenter.Full(q, "quality", "quality_gates")
            : SeatOrganPanePresenter.PulseWithResult(
                q, "quality", QualityGates.Snap(docStore, session.ProjectRoot).Pulse);
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
        return SeatOrganPanePresenter.FullOr(
            IdeReviewChannel.Handle(reviewInputs, tileArgs), wantFull, "review", "review_organ");
    }
}
