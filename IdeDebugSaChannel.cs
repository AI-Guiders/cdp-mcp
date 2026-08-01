#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=debug_desk</c> / Meta <c>cdp_debug_sa</c> — Debug-SA (ADR-0011).
/// Partials: View (decide/next), Capture (DAP snap), Models (Snap/norm).
/// </summary>
internal static partial class IdeDebugSaChannel
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

        var active = snap.Stopped
            || snap.ActiveDap
            || verdict is "continue" or "step" or "fix_bp" or "stop_rebuild" or "need_more" or "attach";
        CideDebugDeskLatch.Publish(
            active,
            pulse,
            verdict,
            bpCount: snap.Breakpoints.Count,
            stopped: snap.Stopped,
            activeDap: snap.ActiveDap);

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

}
