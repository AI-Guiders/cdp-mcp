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
        object? seats = null;
        object? tiles = null;
        var pins = SnapshotPins();
        var tileLayout = OptString(args, "layout");
        string?[] seatPinList = [];

        if (seatsMode)
        {
            var seatMap = IdeDeskSeats.Snapshot();
            seatPinList = IdeDeskSeats.Order.Select(s => seatMap[s]).ToArray();
            var fullPane = OptString(args, "pane_full") ?? OptString(args, "full_pane");
            var seatsDetail = (OptString(args, "seats_detail") ?? OptString(args, "view_detail") ?? "compact")
                .Trim().ToLowerInvariant();
            string? thrashNote = null;
            // W-spray: seats_detail=full without pane_full= dumps every organ body — refuse.
            if ((seatsDetail is "full" or "panes") && fullPane is not { Length: > 0 })
            {
                thrashNote =
                    "W-spray refused: seats_detail=full needs pane_full=<seat|organ>; using compact (A).";
                seatsDetail = "compact";
            }
            var wantPanes = seatsDetail is "full" or "panes"
                || BoolOr(args, "seats_panes", false)
                || BoolOr(args, "compact", true) == false;
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

                var wantFull = fullPane is { Length: > 0 }
                    && (string.Equals(fullPane, organ, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fullPane, seatId, StringComparison.OrdinalIgnoreCase)
                        || (PinAliases.TryGetValue(fullPane, out var fa)
                            && string.Equals(fa, organ, StringComparison.OrdinalIgnoreCase)));

                // Quiet seat: organs that thrash without cdp_open — no dispatch / no Application Data noise.
                // Plan stays live (WitDB offline). Editor/browser quiet until project.
                if (!hasProject && !wantFull && IdeDeskView.OrganNeedsProject(organ))
                {
                    var quiet = QuietNoProjectPane(organ);
                    var (qOk, qLine) = IdeDeskView.LineFromPane(quiet, false, organ);
                    seatPanes.Add(new SeatPane(seatId, organ, false, false, qOk, qLine, quiet));
                    continue;
                }

                var tileArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var kv in args)
                    tileArgs[kv.Key] = kv.Value;
                tileArgs["go_detail"] = JsonSerializer.SerializeToElement(wantFull ? "full" : "pulse");
                // Cockpit steer must not leak into organ dispatch (else browser gets op=feature → unknown_op).
                tileArgs.Remove("go");
                tileArgs.Remove("do");
                tileArgs.Remove("cmd");
                tileArgs.Remove("line");
                tileArgs.Remove("repl");
                tileArgs.Remove("go_args");
                tileArgs.Remove("tm_op");
                tileArgs.Remove("seat");
                tileArgs.Remove("organ");
                tileArgs.Remove("pin");
                tileArgs.Remove("layout");
                tileArgs.Remove("pins");
                tileArgs.Remove("tiles");
                tileArgs.Remove("pane_full");
                tileArgs.Remove("full_pane");
                tileArgs.Remove("seats_detail");
                tileArgs.Remove("view_detail");
                tileArgs.Remove("desk_detail");
                tileArgs.Remove("nav_detail");
                tileArgs.Remove("locus");
                tileArgs.Remove("focus");
                tileArgs.Remove("mfd");
                tileArgs.Remove("page");
                tileArgs.Remove("pin_clear");
                tileArgs.Remove("clear_pins");
                tileArgs.Remove("seat_clear");
                tileArgs.Remove("clear_seats");

                object pane;
                var planPin = CanonicalOrganPin(organ);
                if (planPin is "plan")
                {
                    // Soft organ — seat must not route through GoMap/cdp_work defaults alone.
                    if (workspaceStore is null)
                    {
                        pane = new
                        {
                            ok = false,
                            go = "plan",
                            error = "no_workspace",
                            hint = "Intent workspace WitDB unavailable."
                        };
                    }
                    else
                    {
                        tileArgs["session_phase"] = JsonSerializer.SerializeToElement(
                            CdpEnumParse.ToWire(session.Phase));
                        var board = IdeTaskManager.Handle(workspaceStore, workspaceState, tileArgs);
                        pane = wantFull
                            ? new
                            {
                                ok = true,
                                go = "plan",
                                tool = "cdp_work",
                                detail = "full",
                                truncated = false,
                                result = board
                            }
                            : board;
                    }
                }
                else if (planPin is "report" or "evidence" or "pfd")
                {
                    var board = IdeReportBoard.Handle(session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "report",
                            tool = "report_board",
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "find_desk" or "search_desk" or "code_search")
                {
                    var board = IdeFindChannel.Handle(docStore, session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "find_desk",
                            tool = IdeFindChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "sa_desk" or "code_sa" or "pre_sa" or "sa_code")
                {
                    var board = IdeSaChannel.Handle(docStore, session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "sa_desk",
                            tool = IdeSaChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "debug_desk" or "dap_sa" or "debug_sa")
                {
                    var board = IdeDebugSaChannel.Handle(session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "debug_desk",
                            tool = IdeDebugSaChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "test_desk" or "test_sa")
                {
                    var board = IdeTestSaChannel.Handle(session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "test_desk",
                            tool = IdeTestSaChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "build_desk" or "ship_desk" or "build_sa" or "ship_sa")
                {
                    var board = IdeBuildSaChannel.Handle(session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "build_desk",
                            tool = IdeBuildSaChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "crm" or "callout" or "crm_panel")
                {
                    var board = IdeCrmChannel.Handle(session, workspaceStore, workspaceState, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "crm",
                            tool = IdeCrmChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "files_desk" or "files" or "explorer" or "fm" or "file_manager")
                {
                    var board = IdeFilesChannel.Handle(docStore, session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "files_desk",
                            tool = IdeFilesChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "ignite_desk" or "ignite" or "autoignite" or "cdt_ignite" or "cdp_ignite")
                {
                    var board = IdeIgniteChannel.Handle(tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "ignite_desk",
                            tool = IdeIgniteChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "webcam_desk" or "webcam" or "camera" or "sense" or "cdp_webcam")
                {
                    var board = IdeWebcamChannel.Handle(session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "webcam_desk",
                            tool = IdeWebcamChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "pressure_desk" or "pressure" or "compact_prep" or "pre_compact"
                         or "cdp_pressure")
                {
                    var board = IdePressureChannel.Handle(session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "pressure_desk",
                            tool = IdePressureChannel.ToolName,
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : new
                        {
                            ok = true,
                            go = "pressure_desk",
                            tool = IdePressureChannel.ToolName,
                            detail = "pulse",
                            pulse = IdePressureChannel.PulseLine(),
                            schema = IdePressureChannel.SchemaVersion,
                            hint = "pane_full= / go_detail=full for checklist dump"
                        };
                }
                else if (planPin is "alert" or "eicas" or "sa")
                {
                    var board = IdeAlertChannel.Handle(alertInputs, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "alert",
                            tool = "alert_channel",
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "problems" or "problem" or "errlist" or "errorlist" or "err" or "diags")
                {
                    var board = IdeProblemsChannel.Handle(docStore, session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "problems",
                            tool = "problems_channel",
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : new
                        {
                            ok = true,
                            go = "problems",
                            detail = "pulse",
                            pulse = IdeProblemsChannel.Build(docStore, session).Pulse,
                            result = board
                        };
                }
                else if (planPin is "plugins" or "plugin" or "vsix")
                {
                    var board = IdePluginsChannel.Handle(docStore, session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "plugins",
                            tool = "plugins_channel",
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : new
                        {
                            ok = true,
                            go = "plugins",
                            detail = "pulse",
                            pulse = IdePluginsChannel.Build().Pulse,
                            result = board
                        };
                }
                else if (planPin is "quality" or "gates")
                {
                    var q = QualityGates.EvaluateStore(docStore, session.ProjectRoot);
                    pane = wantFull
                        ? new { ok = true, go = "quality", tool = "quality_gates", detail = "full", truncated = false, result = q }
                        : new { ok = true, go = "quality", detail = "pulse", pulse = QualityGates.Snap(docStore, session.ProjectRoot).Pulse, result = q };
                }
                else if (planPin is "sys")
                {
                    var board = BuildSysOrgan(session, git, shell, buffer, debug, test, work);
                    pane = wantFull
                        ? new { ok = true, go = "sys", tool = "sys_organ", detail = "full", truncated = false, result = board }
                        : board;
                }
                else if (planPin is "ecl")
                {
                    var board = IdeChkChannel.Handle(chkCtx, tileArgs);
                    pane = wantFull
                        ? new { ok = true, go = "ecl", tool = "ecl_organ", detail = "full", truncated = false, result = board }
                        : board;
                }
                else if (planPin is "qrh")
                {
                    var board = IdeQrhChannel.Handle(chkCtx, tileArgs, chkSnap);
                    pane = wantFull
                        ? new { ok = true, go = "qrh", tool = "qrh_organ", detail = "full", truncated = false, result = board }
                        : board;
                }
                else if (planPin is "review")
                {
                    var reviewInputs = new IdeReviewChannel.Inputs(
                        session, gitDirty, problems.Errors, testsFailed, quality.Fail, quality.Warn, chkSnap);
                    var board = IdeReviewChannel.Handle(reviewInputs, tileArgs);
                    pane = wantFull
                        ? new { ok = true, go = "review", tool = "review_organ", detail = "full", truncated = false, result = board }
                        : board;
                }
                else if (!wantFull && IdeWorldChannel.IsWorldOrgan(planPin))
                {
                    // World channel: reuse cockpit snaps — never re-dispatch scene on every desk pulse.
                    pane = WorldSnapPane(planPin, git, shell, browser, mcpPulse);
                }
                else if (!wantFull && planPin is ("editor_scene" or "buffer_scene" or "editor" or "buffer"))
                {
                    pane = EditorSnapPane(buffer);
                }
                else if (!wantFull && planPin is ("script_scene" or "script" or "probe"))
                {
                    var (sok, sp) = ScriptScene.Pulse(session);
                    pane = new { ok = sok, go = "script_scene", detail = "pulse", pulse = sp, snap = true };
                }
                else
                {
                    pane = await DispatchGoAsync(organ, tileArgs, buffer, focusId, dispatch, cancellationToken)
                        .ConfigureAwait(false);
                }

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
