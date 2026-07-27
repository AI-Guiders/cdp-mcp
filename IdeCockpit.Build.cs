#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Cockpit BuildAsync — peeled from IdeCockpit root.
/// </summary>
internal static partial class IdeCockpit
{
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
        var cmdLine = OptString(args, "cmd") ?? OptString(args, "line") ?? OptString(args, "repl")
            ?? OptString(args, "ccl") ?? OptString(args, "ccc");
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
            // Cheerful sit: sticky report without evidence → plan (not !report).
            CheerIdleReportSeat(session);
        }
        else
            EnsureDefaultLayoutFromSettings();

        object? goResult = replDirect;
        // Buffer before go= so locus=buffer:doc-N can inject path= into reload/keep_disk/disk_peek.
        var buffer = CollectBuffer(docStore.Scene());
        // Soft organs: quality → pressure_desk (extracted).
        IdeCockpitSoftDispatch.TryDispatch(
            ref goVerb, ref goResult, ref mfd,
            session, docStore, workspaceStore, workspaceState, args);

        // Channel: defer soft organs that need Collect* CDS snaps (CIDE wire).
        var deferred = PeekDeferredSoftWants(ref goVerb);

        // World snaps early — seat pulse + scene-only go= reuse (no double/triple organ thrash).
        var git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);
        var shell = CollectShell(shellHabitat.Scene());
        var browser = internetBrowser.Pulse();
        var mcpPulse = mcpOutlet.Pulse();
        var settingsPulse = ideSettings.Pulse();

        // Soft world scene go= (pulse only): place seat, skip DispatchGoAsync.
        var goDetailEarly = (OptString(args, "go_detail") ?? "pulse").Trim().ToLowerInvariant();
        if (goVerb is { Length: > 0 }
            && IdeWorldChannel.IsWorldSceneGo(goVerb)
            && goDetailEarly is not "full"
            && !args.ContainsKey("go_args"))
        {
            var pin = ResolvePinName(goVerb.Trim()) ?? goVerb.Trim();
            goResult = WorldSnapPane(pin, git, shell, browser, mcpPulse);
            if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(pin))
                IdeDeskSeats.PlaceOrgan(pin);
            goVerb = null;
        }

        if (goVerb is { Length: > 0 })
        {
            var pin = ResolvePinName(goVerb.Trim()) ?? goVerb.Trim();
            goResult = await DispatchGoAsync(goVerb.Trim(), args, buffer, focusId, dispatch, cancellationToken)
                .ConfigureAwait(false);
            if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(pin))
                IdeDeskSeats.PlaceOrgan(pin);
            // Re-collect after organ may have mutated buffers (reload/keep_disk/edit…).
            buffer = CollectBuffer(docStore.Scene());
            // World organs may have mutated habitat — refresh cheap pulses.
            if (IdeWorldChannel.IsWorldOrgan(pin))
            {
                shell = CollectShell(shellHabitat.Scene());
                browser = internetBrowser.Pulse();
                mcpPulse = mcpOutlet.Pulse();
                if (CanonicalOrganPin(pin) is "git_scene")
                    git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);
            }
        }

        var debug = CollectDebug(session);
        var test = CollectTest(session);
        var work = CollectWork(workspaceStore, workspaceState, session);
        var quality = QualityGates.Snap(docStore, session.ProjectRoot);
        var problems = IdeProblemsChannel.Build(docStore, session);
        var gitKnown = git is not null;
        var gitDirty = GitIsDirty(git);
        var testsGreen = test is { Available: true, LastRun: not null, Success: true };
        var testsFailed = test is { Available: true, LastRun: not null, Success: false };
        var sniperOk = !quality.SuggestSniper || EditSniper.HasHold;
        var chkCtx = IdeChkChannel.CtxFrom(
            session, gitKnown, gitDirty, testsGreen, testsFailed,
            problems.Errors == 0, debug.Stopped, debug.ActiveDap, sniperOk);
        var chkSnap = IdeChkChannel.Build(chkCtx);

        // Channel applies deferred soft organs from CDS snaps.
        IdeAlertChannel.Snap alertSnap;
        IdeAlertChannel.Inputs alertInputs;
        (goResult, alertSnap, alertInputs) = ApplyDeferredSoftOrgans(
            deferred, goResult, session, docStore, workspaceStore, workspaceState, args,
            git, shell, buffer, debug, test, work, quality, problems, chkCtx, chkSnap);

        // Soft organs often return full Handle() — honor go_detail=pulse (default) before desk spray.
        goResult = SlimGoResult(goResult, OptString(args, "go_detail"));

        var loci = BuildLoci(session, git, shell, browser, settingsPulse, buffer, debug, test, work, quality);
        var next = BuildNext(session, git, shell, buffer, debug, test, work, focusId, quality, alertSnap, chkSnap, chkCtx);

        // Sniper locus appears when a corridor is held (desk pulse, not organ dump).
        if (EditSniper.HasHold)
        {
            loci.Insert(Math.Min(1, loci.Count), new Locus(
                "edit:sniper",
                "sniper",
                $"aim {EditSniper.PulseLine}",
                "go=target → go=edit_draft | go=scope_clear",
                "target",
                EditSniper.HoldCard()));
        }

        object? focus = null;
        if (!string.IsNullOrWhiteSpace(focusId))
        {
            var hit = loci.FirstOrDefault(l =>
                string.Equals(l.Id, focusId, StringComparison.OrdinalIgnoreCase));
            focus = hit is null
                ? new { ok = false, locus = focusId, reason = "unknown_locus", hint = "Pick id from loci[]." }
                : new
                {
                    ok = true,
                    locus = hit.Id,
                    kind = hit.Kind,
                    pulse = hit.Pulse,
                    drill = hit.Drill,
                    go = hit.Go,
                    detail = hit.Detail
                };
        }

        // No parallel MFD root page — soft organs carry sys/chk/gates.

        var goVerbs = GoMap.Keys
            .Concat(["quality", "gates", "sys", "chk", "ecl", "qrh", "eqrh", "review", "nav", "tiles", "layout", "tile", "seats", "seat", "repl", "ccl", "tasks", "plan", "feature", "task", "promote", "share", "confirm", "reject", "report", "evidence", "alert", "eicas", "sa", "pressure", "pressure_desk", "compact_prep", "pre_compact", "problems", "problem", "errlist", "errorlist", "err", "diags", "plugins", "plugin", "vsix"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var seatsMode = IdeDeskSeats.IsSeatsMode();
        object? tiles = null;
        var pins = SnapshotPins();
        var tileLayout = OptString(args, "layout");

        if (seatsMode)
        {
            return await BuildSeatsDeskSurfaceAsync(
                session, docStore, workspaceStore, workspaceState, args,
                mfd, focusId, goResult, warm, next, focus, alertSnap, alertInputs,
                loci, goVerbs, git, shell, browser, mcpPulse, buffer, debug, test, work,
                quality, problems, chkCtx, chkSnap, gitDirty, testsFailed,
                dispatch, cancellationToken).ConfigureAwait(false);
        }


        {
            var requestPins = ResolveRequestedPins(args);
            var tilePins = requestPins.Count > 0 ? requestPins : pins;
            if (tilePins.Count > 0)
            {
                var fullPane = OptString(args, "pane_full") ?? OptString(args, "full_pane");
                tiles = await BuildTilesAsync(
                        tilePins,
                        tileLayout,
                        fullPane,
                        args,
                        buffer,
                        focusId,
                        dispatch,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return ComposeTilesSurface(
            session, mfd, tiles, pins, goResult, warm, next, focus, alertSnap,
            loci, goVerbs, args, focusId);
    }
}
