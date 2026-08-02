#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=calendar</c> / Meta <c>cdp_calendar</c> — machine-local date/time + month grid.
/// </summary>
internal static class IdeCalendarChannel
{
    public const string SchemaVersion = "calendar_channel/v0";
    public const string ToolName = "cdp_calendar";
    public const string GoName = "calendar";

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = session;
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "pulse" or "a" or "clock" => Pulse(),
            "month" or "grid" => Month(),
            _ => Scene()
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        return "calendar · " + IdeLocalClock.PulseLine();
    }

    static object Pulse() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "pulse",
        go = GoName,
        tool = ToolName,
        pulse = PulseLine(),
        clock = IdeLocalClock.PulseCard(),
        hint = "Machine-local wall clock. go=calendar for month grid; cockpit slim always has clock=."
    };

    static object Month() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "month",
        go = GoName,
        tool = ToolName,
        pulse = PulseLine(),
        clock = IdeLocalClock.PulseCard(),
        month = IdeLocalClock.MonthAscii(),
        hint = "Today marked [n]. Mon-first grid. Deadlines in clock.deadlines[]."
    };

    static object Scene() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "scene",
        go = GoName,
        tool = ToolName,
        detail = "pulse",
        pulse = PulseLine(),
        clock = IdeLocalClock.PulseCard(),
        month = IdeLocalClock.MonthAscii(),
        hint = "A=pulse clock; C=op=month / pane_full=. Local TZ of the MCP host machine."
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
    }
}
