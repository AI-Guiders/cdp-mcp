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

/// <summary>
/// Surface role for cockpit BuildAsync (CIDE ADR 0036 / arch_desk wire).
/// Fills seat panes (organ bodies) then compositor projects into desk JSON.
/// Soft-organ resolve: <see cref="ResolveSeatOrganPaneAsync"/>.
/// Orthogonal to IDS overlays (ADR 0079).
/// </summary>
internal static partial class IdeCockpit
{
    static readonly SeatsDetailGateUnit SeatsDetailGate = new();
    static readonly SeatOrganArgsSanitizer SeatOrganArgs = new();
    static readonly SeatFullPaneMatchUnit SeatFullPaneMatch = new();

    /// <summary>Collect seat organ panes + compose seats desk surface.</summary>
    private static async Task<string> BuildSeatsDeskSurfaceAsync(
        SessionContext session,
        DocumentBufferStore docStore,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        string mfd,
        string? focusId,
        object? goResult,
        object? warm,
        object next,
        object? focus,
        IdeAlertChannel.Snap alertSnap,
        IdeAlertChannel.Inputs alertInputs,
        IReadOnlyList<Locus> loci,
        string[] goVerbs,
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
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var seatMap = IdeDeskSeats.Snapshot();
        var seatPinList = IdeDeskSeats.Order.Select(s => seatMap[s]).ToArray();
        var fullPane = OptString(args, "pane_full") ?? OptString(args, "full_pane");
        var detailGate = SeatsDetailGate.Compute(new SeatsDetailGateUnit.Input(
            SeatsDetailRaw: OptString(args, "seats_detail") ?? OptString(args, "view_detail"),
            FullPane: fullPane,
            SeatsPanesFlag: BoolOr(args, "seats_panes", false),
            CompactDefaultTrue: BoolOr(args, "compact", true)));
        var thrashNote = detailGate.ThrashNote;
        var wantPanes = detailGate.WantPanes;
        var hasProject = !string.IsNullOrWhiteSpace(session.ProjectRoot);
        var seatPanes = new List<SeatPane>();
        foreach (var seatId in IdeDeskSeats.Order)
        {
            var organ = seatMap[seatId];
            if (organ is not { Length: > 0 })
            {
                seatPanes.Add(new SeatPane(seatId, null, true, false, true, "(empty)", null));
                continue;
            }

            var wantFull = SeatFullPaneMatch.Matches(fullPane, seatId, organ, PinAliases);

            // Compact / one-pane: do not Resolve every seat (that was the hang spray).
            if ((!wantPanes && !wantFull) || (fullPane is { Length: > 0 } && !wantFull))
            {
                seatPanes.Add(new SeatPane(
                    seatId, organ, false, false, true, IdeDeskView.ShortOrgan(organ), null));
                continue;
            }

            // Quiet seat: organs that thrash without cdp_open — no dispatch / no Application Data noise.
            // Plan stays live (WitDB offline). Editor/browser quiet until project.
            if (!hasProject && !wantFull && IdeDeskView.OrganNeedsProject(organ))
            {
                var quiet = QuietNoProjectPane(organ);
                var (qOk, qLine) = IdeDeskView.LineFromPane(quiet, false, organ);
                seatPanes.Add(new SeatPane(seatId, organ, false, false, qOk, qLine, quiet));
                continue;
            }

            var tileArgs = SeatOrganArgs.Sanitize(args, wantFull);
            var planPin = CanonicalOrganPin(organ);
            var pane = await ResolveSeatOrganPaneAsync(
                    organ, planPin, wantFull, tileArgs,
                    session, docStore, workspaceStore, workspaceState, alertInputs,
                    git, shell, browser, mcpPulse, buffer, debug, test, work,
                    quality, problems, chkCtx, chkSnap, gitDirty, testsFailed,
                    focusId, dispatch, cancellationToken)
                .ConfigureAwait(false);

            var (ok, line) = IdeDeskView.LineFromPane(pane, false, organ);
            seatPanes.Add(new SeatPane(seatId, organ, false, wantFull, ok, line, pane));
            if (wantFull)
                wantPanes = true;
        }

        // Compositor: project seat panes + CDS pulses into desk surface.
        return ComposeSeatsSurface(
            session, mfd, seatPanes, wantPanes, seatPinList,
            goResult, warm, next, focus, alertSnap, thrashNote,
            loci, goVerbs, args, focusId);
    }
}
