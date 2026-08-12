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

/// <summary>Soft-instrument seat pane resolve — alias → ISoftInstrumentBoard → Present; else fallback snap/dispatch.</summary>
internal static partial class IdeCockpit
{
    static readonly SoftInstrumentAliasCatalog SoftInstrumentAliases = new();
    static readonly SoftInstrumentBoardMetaCatalog SoftInstrumentMeta = new();
    static readonly SeatFallbackSnapUnit SeatFallbackSnap = new();

    static async Task<object> ResolveSeatInstrumentPaneAsync(
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
        if (SoftInstrumentAliases.TryResolve(planPin) is SoftInstrumentKind kind)
        {
            ISoftInstrumentBoard board = new IdeSoftInstrumentBoard(new SoftInstrumentSeatBag(
                tileArgs,
                session,
                docStore,
                workspaceStore,
                workspaceState,
                Extras: new SoftInstrumentSeatExtras(
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
        SoftInstrumentKind kind,
        object board,
        bool wantFull,
        string? pulse = null,
        string? schema = null) =>
        SeatInstrumentPanePresenter.Present(SoftInstrumentMeta.Require(kind), wantFull, board, pulse, schema);

    static object BuildScriptSnapPane(SessionContext session)
    {
        var (sok, sp) = ScriptScene.Pulse(session);
        return new { ok = sok, go = "script_scene", detail = "pulse", pulse = sp, snap = true };
    }
}
