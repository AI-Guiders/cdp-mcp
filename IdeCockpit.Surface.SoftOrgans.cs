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

/// <summary>Soft-organ seat pane resolve — alias → ISoftOrganBoard → Present; else fallback snap/dispatch.</summary>
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
            ISoftOrganBoard board = new IdeSoftOrganBoard(new SoftOrganSeatBag(
                tileArgs,
                session,
                docStore,
                workspaceStore,
                workspaceState,
                Extras: new SoftOrganSeatExtras(
                    alertInputs,
                    () => BuildSysOrgan(session, git, shell, buffer, debug, test, work),
                    chkCtx,
                    chkSnap,
                    gitDirty,
                    problems,
                    testsFailed,
                    quality)));
            var hit = board.Build(kind);
            return Present(kind, hit.Board, wantFull, hit.Pulse, hit.Schema);
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
}
