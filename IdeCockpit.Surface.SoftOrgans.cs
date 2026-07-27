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
    static readonly SeatFallbackSnapUnit SeatFallbackSnap = new();

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
                SoftOrganKind.Report => Present(
                    kind, IdeReportBoard.Handle(session, tileArgs), wantFull),
                SoftOrganKind.FindDesk => Present(
                    kind, IdeFindChannel.Handle(docStore, session, tileArgs), wantFull),
                SoftOrganKind.SaDesk => Present(
                    kind, IdeSaChannel.Handle(docStore, session, tileArgs), wantFull),
                SoftOrganKind.DebugDesk => Present(
                    kind, IdeDebugSaChannel.Handle(session, tileArgs), wantFull),
                SoftOrganKind.TestDesk => Present(
                    kind, IdeTestSaChannel.Handle(session, tileArgs), wantFull),
                SoftOrganKind.BuildDesk => Present(
                    kind, IdeBuildSaChannel.Handle(session, tileArgs), wantFull),
                SoftOrganKind.Crm => Present(
                    kind, IdeCrmChannel.Handle(session, workspaceStore, workspaceState, tileArgs), wantFull),
                SoftOrganKind.FilesDesk => Present(
                    kind, IdeFilesChannel.Handle(docStore, session, tileArgs), wantFull),
                SoftOrganKind.IgniteDesk => Present(
                    kind, IdeIgniteChannel.Handle(tileArgs), wantFull),
                SoftOrganKind.WebcamDesk => Present(
                    kind, IdeWebcamChannel.Handle(session, tileArgs), wantFull),
                SoftOrganKind.PressureDesk => Present(
                    kind, IdePressureChannel.Handle(session, tileArgs), wantFull,
                    IdePressureChannel.PulseLine(), IdePressureChannel.SchemaVersion),
                SoftOrganKind.OnboardDesk => Present(
                    kind, IdeOnboardChannel.Handle(session, tileArgs), wantFull,
                    IdeOnboardChannel.PulseLine(session), IdeOnboardChannel.SchemaVersion),
                SoftOrganKind.Toolchain => Present(
                    kind, IdeToolchainChannel.Handle(session, tileArgs), wantFull,
                    IdeToolchainChannel.PulseLine(session), IdeToolchainChannel.SchemaVersion),
                SoftOrganKind.Alert => Present(
                    kind, IdeAlertChannel.Handle(alertInputs, tileArgs), wantFull),
                SoftOrganKind.Problems => Present(
                    kind, IdeProblemsChannel.Handle(docStore, session, tileArgs), wantFull,
                    IdeProblemsChannel.Build(docStore, session).Pulse),
                SoftOrganKind.Plugins => Present(
                    kind, IdePluginsChannel.Handle(docStore, session, tileArgs), wantFull,
                    IdePluginsChannel.Build().Pulse),
                SoftOrganKind.Quality => Present(
                    kind, QualityGates.EvaluateStore(docStore, session.ProjectRoot), wantFull,
                    QualityGates.Snap(docStore, session.ProjectRoot).Pulse),
                SoftOrganKind.Sys => Present(
                    kind, BuildSysOrgan(session, git, shell, buffer, debug, test, work), wantFull),
                SoftOrganKind.Ecl => Present(
                    kind, IdeChkChannel.Handle(chkCtx, tileArgs), wantFull),
                SoftOrganKind.Qrh => Present(
                    kind, IdeQrhChannel.Handle(chkCtx, tileArgs, chkSnap), wantFull),
                SoftOrganKind.Review => ResolveReviewPane(
                    wantFull, session, tileArgs, gitDirty, problems, testsFailed, quality, chkSnap),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        var snap = SeatFallbackSnap.Classify(new SeatFallbackSnapUnit.Input(
            planPin, wantFull, IdeWorldChannel.IsWorldOrgan(planPin)));
        return snap switch
        {
            SeatFallbackSnapUnit.SnapKind.World => WorldSnapPane(planPin, git, shell, browser, mcpPulse),
            SeatFallbackSnapUnit.SnapKind.Editor => EditorSnapPane(buffer),
            SeatFallbackSnapUnit.SnapKind.Script => BuildScriptSnapPane(session),
            _ => await DispatchGoAsync(organ, tileArgs, buffer, focusId, dispatch, cancellationToken)
                .ConfigureAwait(false)
        };
    }

    static object Present(
        SoftOrganKind kind,
        object board,
        bool wantFull,
        string? pulse = null,
        string? schema = null) =>
        SeatOrganPanePresenter.Present(SoftOrganMeta.Require(kind), wantFull, board, pulse, schema);

    static object BuildScriptSnapPane(SessionContext session)
    {
        var (sok, sp) = ScriptScene.Pulse(session);
        return new { ok = sok, go = "script_scene", detail = "pulse", pulse = sp, snap = true };
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
        return Present(SoftOrganKind.Plan, IdeTaskManager.Handle(workspaceStore, workspaceState, tileArgs), wantFull);
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
        return Present(SoftOrganKind.Review, IdeReviewChannel.Handle(reviewInputs, tileArgs), wantFull);
    }
}
