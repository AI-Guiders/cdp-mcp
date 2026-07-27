#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Soft-organ <c>go=</c> dispatch extracted from <see cref="IdeCockpit.BuildAsync"/> —
/// quality → pressure_desk (before deferred sys/chk and plan). Behavior-identical move.
/// </summary>
internal static class IdeCockpitSoftDispatch
{
    public static void TryDispatch(
        ref string? goVerb,
        ref object? goResult,
        ref string mfd,
        SessionContext session,
        DocumentBufferStore docStore,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("quality", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("gates", StringComparison.OrdinalIgnoreCase)))
        {
            // Soft organ: quality gates scene (not a separate MCP tool in v0).
            mfd = "gates";
            var path = OptString(args, "path");
            if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
                && ga.TryGetProperty("path", out var gp) && gp.ValueKind == JsonValueKind.String)
                path ??= gp.GetString();
            var q = string.IsNullOrWhiteSpace(path)
                ? QualityGates.EvaluateStore(docStore, session.ProjectRoot)
                : QualityGates.EvaluatePath(docStore, session.ProjectRoot, path!);
            goResult = new
            {
                ok = true,
                go = "quality",
                tool = "quality_gates",
                detail = "full",
                truncated = false,
                result = q
            };
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("quality");
            goVerb = null;
        }

        // Soft organ: report / evidence board (ADR 0193 — last probe body).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("report", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("evidence", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("pfd", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeReportBoard.Handle(session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("report");
            goVerb = null;
        }

        // Soft organ: agent-native search (ADR-0009).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("find_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("search_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("code_search", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeFindChannel.Handle(docStore, session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("find_desk");
            goVerb = null;
        }

        // Soft organ: agent-native code SA (ADR-0010) — not EICAS go=sa.
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("sa_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("code_sa", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("pre_sa", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("sa_code", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeSaChannel.Handle(docStore, session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("sa_desk");
            goVerb = null;
        }

        // Soft organ: agent-native Debug-SA (ADR-0011) — not go=debug raw scene.
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("debug_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("dap_sa", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("debug_sa", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeDebugSaChannel.Handle(session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("debug_desk");
            goVerb = null;
        }

        // Soft organ: agent-native Test-SA (ADR-0012) — not go=test raw scene.
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("test_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("test_sa", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeTestSaChannel.Handle(session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("test_desk");
            goVerb = null;
        }

        // Soft organ: agent-native Build-Ship-SA (ADR-0013) — not go=build/ship actuators.
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("build_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("ship_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("build_sa", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("ship_sa", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeBuildSaChannel.Handle(session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("build_desk");
            goVerb = null;
        }

        // Soft organ: CRM callout panel (ADR-0014) — closed codes, not chat reject essays.
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("crm", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("callout", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("crm_panel", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeCrmChannel.Handle(session, workspaceStore, workspaceState, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("crm");
            goVerb = null;
        }

        // Soft organ: File Manager (ADR-0016) — utility browse project|external; not shell ls.
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("files_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("files", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("fm", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("file_manager", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeFilesChannel.Handle(docStore, session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("files_desk");
            goVerb = null;
        }

        // Soft organ: AutoIgnition CDT → Cursor Composer (Voice→Send).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("ignite_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("ignite", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("autoignite", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("cdt_ignite", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("cdp_ignite", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeIgniteChannel.Handle(args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("ignite_desk");
            goVerb = null;
        }

        // Soft organ: webcam sense (Shared Core in-proc).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("webcam_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("webcam", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("camera", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("sense", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("cdp_webcam", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeWebcamChannel.Handle(session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("webcam_desk");
            goVerb = null;
        }

        // Soft organ: L1 pre-compact pressure prep (AutoIgnition / Task Manager / CDP stash).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("pressure_desk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("pressure", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("compact_prep", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("pre_compact", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("cdp_pressure", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdePressureChannel.Handle(session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("pressure_desk");
            goVerb = null;
        }
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
