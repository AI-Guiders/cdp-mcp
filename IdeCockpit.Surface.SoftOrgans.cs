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
        if (planPin is "plan")
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

        if (planPin is "report" or "evidence" or "pfd")
            return SeatOrganPanePresenter.FullOr(
                IdeReportBoard.Handle(session, tileArgs), wantFull, "report", "report_board");

        if (planPin is "find_desk" or "search_desk" or "code_search")
            return SeatOrganPanePresenter.FullOr(
                IdeFindChannel.Handle(docStore, session, tileArgs), wantFull, "find_desk", IdeFindChannel.ToolName);

        if (planPin is "sa_desk" or "code_sa" or "pre_sa" or "sa_code")
            return SeatOrganPanePresenter.FullOr(
                IdeSaChannel.Handle(docStore, session, tileArgs), wantFull, "sa_desk", IdeSaChannel.ToolName);

        if (planPin is "debug_desk" or "dap_sa" or "debug_sa")
            return SeatOrganPanePresenter.FullOr(
                IdeDebugSaChannel.Handle(session, tileArgs), wantFull, "debug_desk", IdeDebugSaChannel.ToolName);

        if (planPin is "test_desk" or "test_sa")
            return SeatOrganPanePresenter.FullOr(
                IdeTestSaChannel.Handle(session, tileArgs), wantFull, "test_desk", IdeTestSaChannel.ToolName);

        if (planPin is "build_desk" or "ship_desk" or "build_sa" or "ship_sa")
            return SeatOrganPanePresenter.FullOr(
                IdeBuildSaChannel.Handle(session, tileArgs), wantFull, "build_desk", IdeBuildSaChannel.ToolName);

        if (planPin is "crm" or "callout" or "crm_panel")
            return SeatOrganPanePresenter.FullOr(
                IdeCrmChannel.Handle(session, workspaceStore, workspaceState, tileArgs),
                wantFull, "crm", IdeCrmChannel.ToolName);

        if (planPin is "files_desk" or "files" or "explorer" or "fm" or "file_manager")
            return SeatOrganPanePresenter.FullOr(
                IdeFilesChannel.Handle(docStore, session, tileArgs), wantFull, "files_desk", IdeFilesChannel.ToolName);

        if (planPin is "ignite_desk" or "ignite" or "autoignite" or "cdt_ignite" or "cdp_ignite")
            return SeatOrganPanePresenter.FullOr(
                IdeIgniteChannel.Handle(tileArgs), wantFull, "ignite_desk", IdeIgniteChannel.ToolName);

        if (planPin is "webcam_desk" or "webcam" or "camera" or "sense" or "cdp_webcam")
            return SeatOrganPanePresenter.FullOr(
                IdeWebcamChannel.Handle(session, tileArgs), wantFull, "webcam_desk", IdeWebcamChannel.ToolName);

        if (planPin is "pressure_desk" or "pressure" or "compact_prep" or "pre_compact" or "cdp_pressure")
        {
            var board = IdePressureChannel.Handle(session, tileArgs);
            return wantFull
                ? SeatOrganPanePresenter.Full(board, "pressure_desk", IdePressureChannel.ToolName)
                : SeatOrganPanePresenter.Pulse(
                    "pressure_desk", IdePressureChannel.ToolName, IdePressureChannel.PulseLine(),
                    IdePressureChannel.SchemaVersion, "pane_full= / go_detail=full for checklist dump");
        }

        if (planPin is "onboard_desk" or "explore_desk" or "onboard" or "explore" or "cdp_onboard")
        {
            return wantFull
                ? SeatOrganPanePresenter.Full(
                    IdeOnboardChannel.Handle(session, tileArgs), "onboard_desk", IdeOnboardChannel.ToolName)
                : SeatOrganPanePresenter.Pulse(
                    "onboard_desk", IdeOnboardChannel.ToolName, IdeOnboardChannel.PulseLine(session),
                    IdeOnboardChannel.SchemaVersion, "pane_full= / go_detail=full · op=scan to refresh");
        }

        if (planPin is "toolchain" or "toolchain_desk" or "cdp_toolchain"
            or "toolchain_ensure" or "toolchain_probe")
        {
            return wantFull
                ? SeatOrganPanePresenter.Full(
                    IdeToolchainChannel.Handle(session, tileArgs), "toolchain", IdeToolchainChannel.ToolName)
                : SeatOrganPanePresenter.Pulse(
                    "toolchain", IdeToolchainChannel.ToolName, IdeToolchainChannel.PulseLine(session),
                    IdeToolchainChannel.SchemaVersion, "pane_full= / go_detail=full · op=ensure id=python|gcc|…");
        }

        if (planPin is "alert" or "eicas" or "sa")
            return SeatOrganPanePresenter.FullOr(
                IdeAlertChannel.Handle(alertInputs, tileArgs), wantFull, "alert", "alert_channel");

        if (planPin is "problems" or "problem" or "errlist" or "errorlist" or "err" or "diags")
        {
            var board = IdeProblemsChannel.Handle(docStore, session, tileArgs);
            return wantFull
                ? SeatOrganPanePresenter.Full(board, "problems", "problems_channel")
                : SeatOrganPanePresenter.PulseWithResult(
                    board, "problems", IdeProblemsChannel.Build(docStore, session).Pulse);
        }

        if (planPin is "plugins" or "plugin" or "vsix")
        {
            var board = IdePluginsChannel.Handle(docStore, session, tileArgs);
            return wantFull
                ? SeatOrganPanePresenter.Full(board, "plugins", "plugins_channel")
                : SeatOrganPanePresenter.PulseWithResult(
                    board, "plugins", IdePluginsChannel.Build().Pulse);
        }

        if (planPin is "quality" or "gates")
        {
            var q = QualityGates.EvaluateStore(docStore, session.ProjectRoot);
            return wantFull
                ? SeatOrganPanePresenter.Full(q, "quality", "quality_gates")
                : SeatOrganPanePresenter.PulseWithResult(
                    q, "quality", QualityGates.Snap(docStore, session.ProjectRoot).Pulse);
        }

        if (planPin is "sys")
            return SeatOrganPanePresenter.FullOr(
                BuildSysOrgan(session, git, shell, buffer, debug, test, work), wantFull, "sys", "sys_organ");

        if (planPin is "ecl")
            return SeatOrganPanePresenter.FullOr(
                IdeChkChannel.Handle(chkCtx, tileArgs), wantFull, "ecl", "ecl_organ");

        if (planPin is "qrh")
            return SeatOrganPanePresenter.FullOr(
                IdeQrhChannel.Handle(chkCtx, tileArgs, chkSnap), wantFull, "qrh", "qrh_organ");

        if (planPin is "review")
        {
            var reviewInputs = new IdeReviewChannel.Inputs(
                session, gitDirty, problems.Errors, testsFailed, quality.Fail, quality.Warn, chkSnap);
            return SeatOrganPanePresenter.FullOr(
                IdeReviewChannel.Handle(reviewInputs, tileArgs), wantFull, "review", "review_organ");
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
}
