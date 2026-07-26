#nullable enable
using System.Text.Json;
using Cdp.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=debug_desk</c> / Meta <c>cdp_debug_sa</c> — agent-native Debug-SA (ADR-0011).
/// Not <c>go=debug</c> (raw scene) and not EICAS <c>go=sa</c>.
/// </summary>
internal static class IdeDebugSaChannel
{
    public const string SchemaVersion = "debug_sa/v1";
    public const string ToolName = "cdp_debug_sa";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var depth = NormDepth(Opt(args, "depth") ?? "slim");
        var scope = NormScope(Opt(args, "scope") ?? "session");
        var snap = Capture(session);

        var (verdict, why) = Decide(snap, scope);
        var pulse = PulseLine(snap, verdict);

        if (depth == "pulse")
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "debug_desk",
                go = "debug_desk",
                tool = ToolName,
                detail = "pulse",
                pulse,
                verdict,
                why,
                scope,
                next = BuildNext(snap, verdict),
                hint = "depth=slim for bp/launch card. go=debug = raw scene."
            };
        }

        var card = new
        {
            active_dap = snap.ActiveDap,
            stopped = snap.Stopped,
            last_stopped_thread_id = snap.LastStoppedThreadId,
            exception = snap.LastException,
            bp_count = snap.Breakpoints.Count,
            workspace = snap.Workspace,
            target = snap.Target,
            launch_path = snap.LaunchPath,
            launch_ok = snap.LaunchPath is { Length: > 0 } && File.Exists(snap.LaunchPath),
            note = snap.Note
        };

        object? bps = null;
        if (depth == "full" || scope == "bp")
        {
            bps = snap.Breakpoints.Take(depth == "full" ? 40 : 12).Select(b => new
            {
                path = b.File,
                line = b.Line,
                condition = b.Condition
            }).ToArray();
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "debug_desk",
            go = "debug_desk",
            tool = ToolName,
            detail = depth,
            pulse,
            verdict,
            why,
            scope,
            depth,
            dap = card,
            breakpoints = bps,
            next = BuildNext(snap, verdict),
            hint = depth == "full"
                ? "Full bp list. For frame/locals: cdp_debug op=stop_context (async evidence)."
                : "Slim Debug-SA. depth=full for bp list; stop_context via next when stopped."
        };
    }

    static string PulseLine(Snap snap, string verdict)
    {
        if (!snap.ActiveDap && !snap.Stopped)
            return $"debug_desk · {verdict} · idle · bp={snap.Breakpoints.Count}";
        if (snap.Stopped)
            return $"debug_desk · {verdict} · STOPPED t={snap.LastStoppedThreadId} · bp={snap.Breakpoints.Count}";
        return $"debug_desk · {verdict} · dap active · bp={snap.Breakpoints.Count}";
    }

    static (string Verdict, string Why) Decide(Snap snap, string scope)
    {
        if (snap.Workspace is not { Length: > 0 })
            return ("need_more", "No project — cdp_open or workspace_path before DAP.");

        if (snap.Stopped)
        {
            if (snap.LastException is { Length: > 0 })
                return ("step", "Stopped with exception — stop_context then step; avoid blind continue.");
            if (scope == "bp" && snap.Breakpoints.Count == 0)
                return ("fix_bp", "Stopped but no stored breakpoints for target — verify bp bind.");
            return ("continue", "DAP stopped — continue or step; prefer stop_context if locus unclear.");
        }

        if (snap.ActiveDap)
            return ("stop_rebuild", "DAP active (not stopped) — debug_stop before rebuild (PDB lock).");

        // idle
        var launchOk = snap.LaunchPath is { Length: > 0 } && File.Exists(snap.LaunchPath);
        if (!launchOk && snap.Target is { Length: > 0 })
            return ("need_more", "No launch dll resolved — build first or check target_path.");

        if (snap.Breakpoints.Count == 0)
            return ("fix_bp", "Idle DAP — set bp before launch, or attach with process_id.");

        if (scope == "stop")
            return ("attach", "Idle — launch or attach to hit bp (attach needs process_id)." );

        return ("idle", "No active DAP — launch when ready, or attach.");
    }

    static object[] BuildNext(Snap snap, string verdict)
    {
        var list = new List<object>();

        switch (verdict)
        {
            case "continue":
                list.Add(new { go = "debug", label = "stop_context", why = "op=stop_context — frame/locals" });
                list.Add(new { go = "debug", label = "continue", why = "op=continue" });
                list.Add(new { go = "debug", label = "step_over", why = "op=step_over" });
                break;
            case "step":
                list.Add(new { go = "debug", label = "step_over", why = "op=step_over" });
                list.Add(new { go = "debug", label = "step_into", why = "op=step_into" });
                break;
            case "stop_rebuild":
                list.Add(new { go = "debug", label = "debug_stop", why = "op=stop — release PDB" });
                list.Add(new { go = "build", label = "Rebuild", why = "after stop" });
                list.Add(new { go = "qrh", label = "QRH dap-pdb-lock", why = "procedure" });
                break;
            case "fix_bp":
                list.Add(new { go = "debug", label = "bp_add", why = "op=bp_add path+line" });
                list.Add(new { go = "debug", label = "bp_list", why = "op=bp_list" });
                break;
            case "attach":
                list.Add(new { go = "debug", label = "launch", why = "op=launch" });
                list.Add(new { go = "debug", label = "attach", why = "op=attach process_id=" });
                break;
            case "idle":
                list.Add(new { go = "debug", label = "launch", why = "op=launch" });
                list.Add(new { go = "debug", label = "scene", why = "op=scene" });
                break;
            default:
                list.Add(new { go = "debug", label = "scene", why = "op=scene" });
                list.Add(new { go = "open", label = "cdp_open", why = "root project" });
                break;
        }

        list.Add(new { go = "alert", label = "EICAS", why = "attention SA (1-bit DAP)" });
        list.Add(new { go = "ecl", label = "ECL dap-rebuild", why = "checklist" });

        if (snap.Stopped)
            list.Insert(0, new { go = "debug", label = "stop_context", why = "evidence before act" });

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<object>();
        foreach (var item in list)
        {
            var t = item.GetType();
            var key = (t.GetProperty("label")?.GetValue(item) as string ?? "") + "\0" +
                      (t.GetProperty("why")?.GetValue(item) as string ?? "");
            if (!seen.Add(key)) continue;
            deduped.Add(item);
        }

        return deduped.ToArray();
    }

    static Snap Capture(SessionContext session)
    {
        var ws = session.ProjectRoot ?? session.ScmRoot;
        var target = session.SolutionOrProjectPath;
        string? note = ws is null ? "No session project — cdp_open first." : null;

        IReadOnlyList<BreakpointsStorage.BreakpointEntry> bps =
            Array.Empty<BreakpointsStorage.BreakpointEntry>();
        if (ws is { Length: > 0 } && target is { Length: > 0 })
        {
            try { bps = BreakpointsStorage.GetBreakpoints(ws, target); }
            catch { /* ignore */ }
        }

        string? launch = null;
        if (target is { Length: > 0 })
        {
            try { launch = LaunchTargetResolver.TryResolveBinary(target); }
            catch { /* ignore */ }
        }

        return new Snap(
            ws,
            target,
            launch,
            note,
            DebugSession.CurrentClient is not null,
            DebugSession.LastStoppedThreadId > 0,
            DebugSession.LastStoppedThreadId,
            DebugSession.LastExceptionText,
            bps);
    }

    static string NormDepth(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pulse" or "p" => "pulse",
        "full" or "raw" or "deep" => "full",
        _ => "slim"
    };

    static string NormScope(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "bp" or "breakpoints" => "bp",
        "stop" or "stopped" => "stop",
        _ => "session"
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    sealed record Snap(
        string? Workspace,
        string? Target,
        string? LaunchPath,
        string? Note,
        bool ActiveDap,
        bool Stopped,
        int LastStoppedThreadId,
        string? LastException,
        IReadOnlyList<BreakpointsStorage.BreakpointEntry> Breakpoints);
}
