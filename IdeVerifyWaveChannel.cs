#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=verify_wave</c> / Meta <c>cdp_verify_wave</c> — ship checklist pulse.
/// Does NOT auto KillRunning deploy from in-proc CDP.
/// </summary>
internal static class IdeVerifyWaveChannel
{
    public const string SchemaVersion = "verify_wave_channel/v1";
    public const string ToolName = "cdp_verify_wave";
    public const string GoName = "verify_wave";

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "pulse" or "a" => Pulse(),
            _ => Scene(session)
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var wave = IdeWaveChannel.PulseLine();
        return $"verify_wave · checklist · {wave}";
    }

    static object Scene(SessionContext session)
    {
        var checks = new object[]
        {
            new
            {
                id = "tests",
                status = "todo",
                note = "Filter/run new wave+inventory+pressure tests (terminal_* if cdp_test DLL-locks seat)."
            },
            new
            {
                id = "version",
                status = "todo",
                note = "Bump CdpMcp.csproj Version; confirm both seats after deploy."
            },
            new
            {
                id = "dual_hard",
                status = "todo",
                note =
                    "Dual hard via sibling terminal_* + publish-and-deploy.ps1 -Mode hard — NEVER in-proc cdp_shell (KillRunning)."
            },
            new
            {
                id = "dogfood",
                status = "todo",
                note = "wave seed → inventory scene → verify_wave → pressure stash wave=."
            },
            new
            {
                id = "domain_stamp",
                status = "todo",
                note = "Stamp .cdp/domain/throughput.md (+ light pressure/ignite/citizen)."
            },
            new
            {
                id = "git",
                status = "todo",
                note = "Logical commits + push (feat / docs / tests split)."
            },
            new
            {
                id = "ignite_rearm",
                status = "todo",
                note = "cdp_ignite op=arm when=timer last_once — insurance, not nap."
            }
        };

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = PulseLine(session),
            wave = IdeWaveChannel.PulseLine(),
            project_root = session.ProjectRoot,
            checklist = checks,
            deploy_recipe = new
            {
                mode = "hard",
                seats = new[] { @"D:\cdp-mcp", @"D:\cdp-mcp-debug" },
                script = "publish-and-deploy.ps1",
                habitat = "terminal_* (escape) — not cdp_shell_* during KillRunning",
                nudge = "Bump env.CDP_RELOAD_NUDGE in ~/.cursor/mcp.json after hard"
            },
            ops = new[] { "scene", "pulse" },
            next = new object[]
            {
                new { go = "inventory", label = "Gaps", why = "op=scene" },
                new { go = "plan", label = "Wave scene", why = "cmd=wave scene" },
                new { go = "ignite_desk", label = "Re-ARM", why = "op=arm when=timer" }
            },
            hint =
                "Checklist only — does not deploy. Soft FileLines CLOSED. list→batch→ship then dual hard from escape hatch."
        };
    }

    static object Pulse() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "pulse",
        go = GoName,
        pulse = PulseLine()
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            _ => el.ToString()
        };
    }
}
