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
/// Cockpit BuildAsync orchestrator — peels: Probes / WorldGo / LegacyTiles / Surface.
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
        object? replDirect = null;
        var transport = IngestCockpitRequest(args);
        args = transport.Args;
        var cmdLine = transport.CmdLine;
        if (cmdLine is { Length: > 0 })
        {
            var applied = IdeRepl.Apply(cmdLine, args);
            if (applied is { } a)
            {
                args = a.Args;
                replDirect = a.Direct;
            }
        }

        var focusId = OptString(args, "locus") ?? OptString(args, "focus");
        var includeSubmodules = BoolOr(args, "include_submodules", false);
        string mfd;
        string? goVerb;
        (mfd, goVerb, args) = NormalizeAttentionRouting(args);

        ApplyDeskMutation(args);
        var deskCleared = BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false)
            || BoolOr(args, "seat_clear", false) || BoolOr(args, "clear_seats", false);
        if (IdeDeskSeats.IsSeatsMode())
        {
            if (!deskCleared)
                IdeDeskSeats.EnsureDefaultsFromSettings();
            CheerIdleReportSeat(session);
        }
        else
            EnsureDefaultLayoutFromSettings();

        object? goResult = replDirect;
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

        var loci = BuildLoci(
            session, git, shell, browser, settingsPulse, buffer,
            probes.Debug, probes.Test, probes.Work, probes.Quality);
        var next = BuildNext(
            session, git, shell, buffer, probes.Debug, probes.Test, probes.Work, focusId,
            probes.Quality, alertSnap, probes.ChkSnap, probes.ChkCtx);

        if (DeskSniperLocus.TryBuild(new DeskSniperLocusUnit.Input(
                EditSniper.HasHold, EditSniper.PulseLine, EditSniper.HoldCard())) is { } sniper)
        {
            loci.Insert(Math.Min(1, loci.Count), new Locus(
                sniper.Id, sniper.Kind, sniper.Pulse, sniper.Drill, sniper.Go, sniper.Detail));
        }

        object? focus = FocusLocus.Build(
            focusId,
            loci.Select(l => new FocusLocusUnit.LocusRef(l.Id, l.Kind, l.Pulse, l.Drill, l.Go, l.Detail)).ToArray());

        var goVerbs = GoVerbsCatalog.Merge(GoMap.Keys);

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
