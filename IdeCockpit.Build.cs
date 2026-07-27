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

        var mfdExplicit = OptString(args, "mfd") ?? OptString(args, "page");
        // Seats: desk.default_mfd deprecated — do not auto-steer organs from settings.
        var mfd = (mfdExplicit
                   ?? (IdeDeskSeats.IsSeatsMode() ? "nav" : IdeSettingsHabitat.EffectiveDeskMfd())
                   ?? "nav")
            .Trim().ToLowerInvariant();
        if (!MfdPages.Contains(mfd))
            mfd = "nav";

        var focusId = OptString(args, "locus") ?? OptString(args, "focus");
        var includeSubmodules = BoolOr(args, "include_submodules", false);
        var goVerb = OptString(args, "go") ?? OptString(args, "do");

        // Legacy MFD pages → seats: nav=desk_detail; sys|chk|gates=soft organs (not root page).
        if (goVerb is { Length: > 0 } && MfdPages.Contains(goVerb.Trim()))
        {
            var pageVerb = goVerb.Trim().ToLowerInvariant();
            mfd = pageVerb;
            if (pageVerb == "nav")
            {
                args = WithStringArg(args, "desk_detail", "nav");
                goVerb = null;
            }
            // else keep goVerb for soft-organ handlers below
        }
        else if (goVerb is null
                 && mfdExplicit is not null
                 && mfd is "sys" or "chk" or "ecl" or "gates")
        {
            // bare mfd=/page= (explicit) → same as go=
            goVerb = mfd;
        }

        // Soft tile / seat verbs: go=tiles|layout|seats|repl (no organ).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("tiles", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("layout", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("tile", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("seats", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("seat", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("repl", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("ccl", StringComparison.OrdinalIgnoreCase)))
        {
            goVerb = null;
        }

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


        // Defer sys/chk/alert until after Collect* snaps.
        var wantSys = goVerb is { Length: > 0 }
            && goVerb.Equals("sys", StringComparison.OrdinalIgnoreCase);
        if (wantSys)
            goVerb = null;

        var wantChk = goVerb is { Length: > 0 }
            && (goVerb.Equals("chk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("ecl", StringComparison.OrdinalIgnoreCase));
        if (wantChk)
            goVerb = null;

        var wantQrh = goVerb is { Length: > 0 }
            && (goVerb.Equals("qrh", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("eqrh", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("handbook", StringComparison.OrdinalIgnoreCase));
        if (wantQrh)
            goVerb = null;

        var wantAlert = goVerb is { Length: > 0 }
            && (goVerb.Equals("alert", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("eicas", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("sa", StringComparison.OrdinalIgnoreCase));
        if (wantAlert)
            goVerb = null;

        var wantProblems = goVerb is { Length: > 0 }
            && (goVerb.Equals("problems", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("problem", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("errlist", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("errorlist", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("err", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("diags", StringComparison.OrdinalIgnoreCase));
        if (wantProblems)
            goVerb = null;

        var wantPlugins = goVerb is { Length: > 0 }
            && (goVerb.Equals("plugins", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("plugin", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("vsix", StringComparison.OrdinalIgnoreCase));
        if (wantPlugins)
            goVerb = null;

        var wantReview = goVerb is { Length: > 0 }
            && goVerb.Equals("review", StringComparison.OrdinalIgnoreCase);
        if (wantReview)
            goVerb = null;

        // Soft organ: Plan / Task Manager (Feature → Task tree, WitDB sticky focus).
        // Plan share: cmd="share plan" / go=plan tm_op=share. Bare go=share → buffer (GoMap).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("plan", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("work", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("tasks", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("tm", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("task", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("feature", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("promote", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("confirm", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("reject", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("phase", StringComparison.OrdinalIgnoreCase)))
        {
            if (workspaceStore is null)
            {
                goResult = new
                {
                    ok = false,
                    go = "plan",
                    error = "no_workspace",
                    hint = "Intent workspace WitDB unavailable."
                };
            }
            else
            {
                var tmArgs = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
                if (session.ProjectRoot is { Length: > 0 } pr)
                    tmArgs["project_root"] = JsonSerializer.SerializeToElement(pr);
                tmArgs["session_phase"] = JsonSerializer.SerializeToElement(CdpEnumParse.ToWire(session.Phase));
                if (!tmArgs.ContainsKey("tm_op")
                    && goVerb is "feature" or "task" or "promote" or "confirm" or "reject"
                    && (!tmArgs.TryGetValue("go_args", out var gax)
                        || gax.ValueKind != JsonValueKind.Object
                        || !gax.TryGetProperty("op", out _)))
                {
                    tmArgs["tm_op"] = JsonSerializer.SerializeToElement(
                        goVerb.Equals("feature", StringComparison.OrdinalIgnoreCase) ? "feature"
                        : goVerb.Equals("task", StringComparison.OrdinalIgnoreCase) ? "task"
                        : goVerb.Equals("promote", StringComparison.OrdinalIgnoreCase) ? "promote"
                        : goVerb.Equals("confirm", StringComparison.OrdinalIgnoreCase) ? "confirm"
                        : goVerb.Equals("reject", StringComparison.OrdinalIgnoreCase) ? "reject"
                        : "board");
                }

                goResult = IdeTaskManager.Handle(workspaceStore, workspaceState, tmArgs);
            }

            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("plan");
            goVerb = null;
        }

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

        // Soft organs that own a seat: place BEFORE SA fuse so same-turn map matches desk.
        // Alert/sa is root EICAS (PulseCard) — do not PlaceOrgan (steals P, frame skew).
        if (wantProblems)
        {
            goResult = IdeProblemsChannel.Handle(docStore, session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("problems");
        }

        if (wantPlugins)
        {
            goResult = IdePluginsChannel.Handle(docStore, session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("plugins");
        }

        if (wantReview)
        {
            var reviewInputs = new IdeReviewChannel.Inputs(
                session,
                gitDirty,
                problems.Errors,
                testsFailed,
                quality.Fail,
                quality.Warn,
                chkSnap);
            goResult = IdeReviewChannel.Handle(reviewInputs, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("review");
        }

        if (wantSys)
        {
            goResult = BuildSysOrgan(session, git, shell, buffer, debug, test, work);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("sys");
        }

        if (wantChk)
        {
            goResult = IdeChkChannel.Handle(chkCtx, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("ecl");
        }

        if (wantQrh)
        {
            goResult = IdeQrhChannel.Handle(chkCtx, args, chkSnap);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("qrh");
        }

        var alertInputs = BuildAlertInputs(
            session, quality, buffer, debug, shell, git, problems, work, workspaceStore, workspaceState, chkSnap);
        var alertSnap = IdeAlertChannel.Build(alertInputs);

        if (wantAlert)
            goResult = IdeAlertChannel.Handle(alertInputs, args);

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

            var viewSlots = seatPanes
                .Select(s => new IdeDeskView.Slot(s.Seat, s.Organ, s.Empty, s.Ok, s.Line, s.Full))
                .ToArray();
            var view = IdeDeskView.Build(viewSlots);
            var includePanes = wantPanes;
            // Root payload owns view once — seats/tiles keep slots only (cockpit/v1.8).
            seats = IdeDeskSeats.Card(
                seatPanes.Select(s => s.ToSlot()).ToList(),
                includePanes ? seatPanes.Select(s => s.ToCard(true)).ToList() : null);
            // Seats mode: no legacy tiles blob (view once at root).
            tiles = null;

            var deskDetail = ResolveDeskDetail(args, focusId);
            var wantNav = deskDetail is "nav" or "full";
            var payload = new Dictionary<string, object?>
            {
                ["schema"] = SchemaVersion,
                ["ok"] = true,
                ["role"] = "desk",
                ["mode"] = "seats",
                ["view"] = view,
                ["mfd"] = mfd,
                ["mfd_note"] = "legacy alias — go=sys|chk|gates|nav (soft organs / desk_detail); no root page",
                ["session"] = SessionPulse(session),
                ["desk_detail"] = deskDetail,
                ["seats"] = seats,
                ["tiles"] = tiles,
                ["pins"] = seatPinList.Where(x => x is { Length: > 0 }).ToArray(),
                ["layouts"] = LayoutPresetIds,
                ["next"] = next,
                ["focus"] = focus,
                ["page"] = null,
                ["go"] = goResult,
                ["warm"] = warm,
                ["alert"] = IdeAlertChannel.PulseCard(alertSnap),
                ["pressure"] = IsPressureGoResult(goResult)
                    ? null
                    : IdePressureChannel.PulseCardOrNull(),
                ["thrash"] = thrashNote,
                ["hint"] = wantNav
                    ? "Read view.banner / view.ascii first. Steer: cmd=\"go sa\" | layout=agent. " +
                      "C: pane_full= one dump; W: seats_detail=full spray."
                    : "Slim desk (cockpit/v1.20): view + seats + next + alert(sa) + pressure?. " +
                      "go=sys|chk|pressure soft organs; desk_detail=nav for loci[]; cmd=sa|alert|pressure|probe|report|plan (CCL). " +
                      "Context W/C/A: A=pulse; C=go_detail=full|pane_full=; W=seats_detail=full."
            };
            if (wantNav)
            {
                payload["loci"] = loci.Select(l => l.Card()).ToArray();
                payload["go_verbs"] = goVerbs;
            }

            return JsonSerializer.Serialize(payload, Pretty);
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

        var deskDetailTiles = ResolveDeskDetail(args, focusId);
        var wantNavTiles = deskDetailTiles is "nav" or "full";
        var tilesPayload = new Dictionary<string, object?>
        {
            ["schema"] = SchemaVersion,
            ["ok"] = true,
            ["role"] = "desk",
            ["mode"] = "tiles",
            ["mfd"] = mfd,
            ["mfd_note"] = "legacy alias — go=sys|chk|gates|nav; seats preferred; no root page",
            ["session"] = SessionPulse(session),
            ["desk_detail"] = deskDetailTiles,
            ["seats"] = null,
            ["tiles"] = tiles,
            ["pins"] = pins.ToArray(),
            ["layouts"] = LayoutPresetIds,
            ["next"] = next,
            ["focus"] = focus,
            ["page"] = null,
            ["go"] = goResult,
            ["warm"] = warm,
            ["alert"] = IdeAlertChannel.PulseCard(alertSnap),
            ["hint"] = "desk.mode=tiles (legacy). Prefer seats. go=sys|chk soft organs; desk_detail=nav for loci."
        };
        if (wantNavTiles)
        {
            tilesPayload["loci"] = loci.Select(l => l.Card()).ToArray();
            tilesPayload["go_verbs"] = goVerbs;
        }

        return JsonSerializer.Serialize(tilesPayload, Pretty);
    }
}

