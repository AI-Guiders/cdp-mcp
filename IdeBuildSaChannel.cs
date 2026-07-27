#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=build_desk</c> / Meta <c>cdp_build_sa</c> — Build-Ship-SA (ADR-0013).
/// Partials: View (decide/next), Capture (git/snap), Models (Snap/norm).
/// </summary>
internal static partial class IdeBuildSaChannel
{
    public const string SchemaVersion = "build_sa/v1";
    public const string ToolName = "cdp_build_sa";

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
        var snap = Capture(session, args);

        var (verdict, why) = Decide(snap, scope);
        var pulse = PulseLine(snap, verdict);

        if (depth == "pulse")
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "build_desk",
                go = "build_desk",
                tool = ToolName,
                detail = "pulse",
                pulse,
                verdict,
                why,
                scope,
                next = BuildNext(snap, verdict),
                hint = "depth=slim for dirty/DAP card. go=build = actuator; go=ship ≠ this organ."
            };
        }

        object? dirty = null;
        if (depth == "full" || scope == "ship" || verdict is "ship" or "preflight")
        {
            dirty = snap.Dirty.Take(depth == "full" ? 40 : 12).Select(f => new
            {
                path = f.Path,
                status = f.Status,
                risk = f.Risk,
                why = f.Why
            }).ToArray();
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "build_desk",
            go = "build_desk",
            tool = ToolName,
            detail = depth,
            pulse,
            verdict,
            why,
            scope,
            depth,
            build = new
            {
                target = snap.Target,
                target_ok = snap.TargetOk,
                active_dap = snap.ActiveDap,
                stopped = snap.Stopped
            },
            scm = new
            {
                root = snap.ScmRoot,
                branch = snap.Branch,
                dirty = snap.Dirty.Count > 0,
                dirty_count = snap.Dirty.Count,
                secret_hits = snap.SecretHits,
                ahead = snap.Ahead,
                behind = snap.Behind
            },
            dirty_files = dirty,
            next = BuildNext(snap, verdict),
            hint = depth == "full"
                ? "Full dirty list. Act via cdp_build / git_plan / git_push — not shell archaeology."
                : "Slim Build-Ship-SA. depth=full for dirty paths; stop DAP before rebuild."
        };
    }

}
