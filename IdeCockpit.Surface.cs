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
/// Orthogonal to IDS overlays (ADR 0079).
/// </summary>
internal static partial class IdeCockpit
{
    static readonly SeatsDetailGateUnit SeatsDetailGate = new();
    static readonly SeatOrganArgsSanitizer SeatOrganArgs = new();
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

            var tileArgs = SeatOrganArgs.Sanitize(args, wantFull);

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
            else if (planPin is "onboard_desk" or "explore_desk" or "onboard" or "explore" or "cdp_onboard")
            {
                if (wantFull)
                {
                    var board = IdeOnboardChannel.Handle(session, tileArgs);
                    pane = new
                    {
                        ok = true,
                        go = "onboard_desk",
                        tool = IdeOnboardChannel.ToolName,
                        detail = "full",
                        truncated = false,
                        result = board
                    };
                }
                else
                {
                    pane = new
                    {
                        ok = true,
                        go = "onboard_desk",
                        tool = IdeOnboardChannel.ToolName,
                        detail = "pulse",
                        pulse = IdeOnboardChannel.PulseLine(session),
                        schema = IdeOnboardChannel.SchemaVersion,
                        hint = "pane_full= / go_detail=full · op=scan to refresh"
                    };
                }
            }
            else if (planPin is "toolchain" or "toolchain_desk" or "cdp_toolchain"
                     or "toolchain_ensure" or "toolchain_probe")
            {
                if (wantFull)
                {
                    var board = IdeToolchainChannel.Handle(session, tileArgs);
                    pane = new
                    {
                        ok = true,
                        go = "toolchain",
                        tool = IdeToolchainChannel.ToolName,
                        detail = "full",
                        truncated = false,
                        result = board
                    };
                }
                else
                {
                    pane = new
                    {
                        ok = true,
                        go = "toolchain",
                        tool = IdeToolchainChannel.ToolName,
                        detail = "pulse",
                        pulse = IdeToolchainChannel.PulseLine(session),
                        schema = IdeToolchainChannel.SchemaVersion,
                        hint = "pane_full= / go_detail=full · op=ensure id=python|gcc|…"
                    };
                }
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
}
