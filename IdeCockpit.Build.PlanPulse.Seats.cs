#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Desk-pulse seats + optional <c>pane_full=</c> one-organ dump.</summary>
internal static partial class IdeCockpit
{
    /// <summary>
    /// Desk-pulse seats + optional <c>pane_full=</c> one-organ dump (no all-seat Resolve spray).
    /// </summary>
    static async Task<string> BuildDeskPulseSeatsSurfaceAsync(
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
        string? resultPin,
        JsonElement? git,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        McpOutletHabitat.McpPulse mcpPulse,
        BufferSnap buffer,
        DeskProbeBundle probes,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var seatMap = IdeDeskSeats.Snapshot();
        var seatPinList = IdeDeskSeats.Order.Select(s => seatMap[s]).ToArray();
        var fullPane = OptString(args, "pane_full") ?? OptString(args, "full_pane");
        var seatPanes = new List<SeatPane>();
        var wantPin = resultPin is { Length: > 0 } ? CanonicalOrganPin(resultPin) : null;
        var wantPanes = false;
        var hasProject = !string.IsNullOrWhiteSpace(session.ProjectRoot);

        foreach (var seatId in IdeDeskSeats.Order)
        {
            var organ = seatMap[seatId];
            if (organ is not { Length: > 0 })
            {
                seatPanes.Add(new SeatPane(seatId, null, true, false, true, "(empty)", null));
                continue;
            }

            var wantFull = SeatFullPaneMatch.Matches(fullPane, seatId, organ, PinAliases);
            if (wantFull)
            {
                if (!hasProject && IdeDeskView.OrganNeedsProject(organ))
                {
                    var quiet = QuietNoProjectPane(organ);
                    var (qOk, qLine) = IdeDeskView.LineFromPane(quiet, false, organ);
                    seatPanes.Add(new SeatPane(seatId, organ, false, true, qOk, qLine, quiet));
                    wantPanes = true;
                    continue;
                }

                var tileArgs = SeatOrganArgs.Sanitize(args, wantFull: true);
                var planPin = CanonicalOrganPin(organ);
                var pane = await ResolveSeatOrganPaneAsync(
                        organ, planPin, wantFull: true, tileArgs,
                        session, docStore, workspaceStore, workspaceState, alertInputs,
                        git, shell, browser, mcpPulse, buffer,
                        probes.Debug, probes.Test, probes.Work, probes.Quality, probes.Problems,
                        probes.ChkCtx, probes.ChkSnap, probes.GitDirty, probes.TestsFailed,
                        focusId, dispatch, cancellationToken)
                    .ConfigureAwait(false);
                var (ok, line) = IdeDeskView.LineFromPane(pane, false, organ);
                seatPanes.Add(new SeatPane(seatId, organ, false, true, ok, line, pane));
                wantPanes = true;
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
            session, mfd, seatPanes, wantPanes, seatPinList,
            goResult, warm, next, focus, alertSnap, DeskPulseWSprayThrash(args),
            loci, goVerbs, args, focusId);
    }
}
