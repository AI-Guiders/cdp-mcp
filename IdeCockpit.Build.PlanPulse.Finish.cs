#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Desk pulse finish path (≤ADX soft-warn peel).</summary>
internal static partial class IdeCockpit
{
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

        (goResult, git, shell, browser, mcpPulse, resultPin) = await ApplyDeskPulseGoAsync(
            goVerb, goResult, resultPin, args, buffer, focusId, session, byDomain, includeSubmodules,
            shellHabitat, internetBrowser, mcpOutlet, git, shell, browser, mcpPulse,
            dispatch, cancellationToken).ConfigureAwait(false);

        var probes = CollectPlanPulseProbeBundle(session, workspaceStore, workspaceState);
        IdeAlertChannel.Snap alertSnap;
        IdeAlertChannel.Inputs alertInputs;
        if (AnyDeferredSoftWant(deferred))
        {
            (goResult, alertSnap, alertInputs) = ApplyDeferredSoftOrgans(
                deferred, goResult, session, docStore, workspaceStore, workspaceState, args,
                git, shell, buffer, probes.Debug, probes.Test, probes.Work, probes.Quality,
                probes.Problems, probes.ChkCtx, probes.ChkSnap,
                publishGlassSpray: false);
            resultPin = TryGoPinFromResult(goResult) ?? resultPin;
        }
        else
        {
            (alertSnap, alertInputs) = ApplyPlanPulseGlass(
                session, workspaceStore, workspaceState, buffer, shell, probes);
        }

        goResult = SlimGoResult(goResult, OptString(args, "go_detail"));

        var (loci, next, focus, goVerbs) = BuildDeskNavigation(
            session, git, shell, browser, settingsPulse, buffer,
            probes.Debug, probes.Test, probes.Work, probes.Quality, focusId,
            alertSnap, probes.ChkSnap, probes.ChkCtx);

        if (IdeDeskSeats.IsSeatsMode())
        {
            return await BuildDeskPulseSeatsSurfaceAsync(
                    session, docStore, workspaceStore, workspaceState, args, mfd, focusId,
                    goResult, warm, next, focus, alertSnap, alertInputs,
                    loci, goVerbs, resultPin, git, shell, browser, mcpPulse, buffer, probes,
                    dispatch, cancellationToken)
                .ConfigureAwait(false);
        }

        return ComposeTilesSurface(
            session, mfd, tiles: null, pins: Array.Empty<string>(), goResult, warm, next, focus,
            alertSnap, loci, goVerbs, args, focusId);
    }

}
