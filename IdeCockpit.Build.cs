#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Cockpit BuildAsync orchestrator — peels: Ingress / Probes / WorldGo / Nav / LegacyTiles / Surface.
/// CCU units under Cockpit/ComputingUnits.
/// </summary>
internal static partial class IdeCockpit
{
    static readonly WorldSceneGoUnit WorldSceneGo = new();
    static readonly FocusLocusUnit FocusLocus = new();
    static readonly GoVerbsCatalogUnit GoVerbsCatalog = new();
    static readonly DeskSniperLocusUnit DeskSniperLocus = new();

    public static async Task<string> BuildAsync(
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
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken,
        object? warm = null)
    {
        var ingress = PrepareBuildIngress(session, args);
        args = ingress.Args;
        var focusId = ingress.FocusId;
        var includeSubmodules = ingress.IncludeSubmodules;
        var mfd = ingress.Mfd;
        var goVerb = ingress.GoVerb;

        object? goResult = ingress.ReplDirect;
        var buffer = CollectBuffer(docStore.Scene());
        IdeCockpitSoftDispatch.TryDispatch(
            ref goVerb, ref goResult, ref mfd,
            session, docStore, workspaceStore, workspaceState, args);

        var deferred = PeekDeferredSoftWants(ref goVerb);

        var git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);
        var shell = CollectShell(shellHabitat.Scene());
        var browser = internetBrowser.Pulse();
        var mcpPulse = mcpOutlet.Pulse();
        var settingsPulse = ideSettings.Pulse();

        (goResult, goVerb, git, shell, browser, mcpPulse) = await ApplyWorldOrGoAsync(
            goVerb, goResult, args, buffer, focusId, session, byDomain, includeSubmodules,
            shellHabitat, internetBrowser, mcpOutlet, git, shell, browser, mcpPulse,
            dispatch, cancellationToken).ConfigureAwait(false);

        // Re-collect buffers after go= may have mutated them.
        buffer = CollectBuffer(docStore.Scene());

        var probes = CollectProbeBundle(session, docStore, workspaceStore, workspaceState, git);

        IdeAlertChannel.Snap alertSnap;
        IdeAlertChannel.Inputs alertInputs;
        (goResult, alertSnap, alertInputs) = ApplyDeferredSoftOrgans(
            deferred, goResult, session, docStore, workspaceStore, workspaceState, args,
            git, shell, buffer, probes.Debug, probes.Test, probes.Work, probes.Quality,
            probes.Problems, probes.ChkCtx, probes.ChkSnap);

        goResult = SlimGoResult(goResult, OptString(args, "go_detail"));

        var (loci, next, focus, goVerbs) = BuildDeskNavigation(
            session, git, shell, browser, settingsPulse, buffer,
            probes.Debug, probes.Test, probes.Work, probes.Quality, focusId,
            alertSnap, probes.ChkSnap, probes.ChkCtx);

        if (IdeDeskSeats.IsSeatsMode())
        {
            return await BuildSeatsDeskSurfaceAsync(
                session, docStore, workspaceStore, workspaceState, args,
                mfd, focusId, goResult, warm, next, focus, alertSnap, alertInputs,
                loci, goVerbs, git, shell, browser, mcpPulse, buffer,
                probes.Debug, probes.Test, probes.Work,
                probes.Quality, probes.Problems, probes.ChkCtx, probes.ChkSnap,
                probes.GitDirty, probes.TestsFailed,
                dispatch, cancellationToken).ConfigureAwait(false);
        }

        return await BuildLegacyTilesDeskAsync(
            session, args, mfd, goResult, warm, next, focus, alertSnap,
            loci, goVerbs, buffer, focusId, dispatch, cancellationToken).ConfigureAwait(false);
    }
}
