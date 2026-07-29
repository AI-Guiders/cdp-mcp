#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=fdr</c> / Meta <c>cdp_fdr</c> — Black-box FDR desk (incident tape, not chat).
/// Ops: scene|tail|stats|slow. VDR (cabin voice) deferred.
/// </summary>
internal static class IdeFdrChannel
{
    public const string SchemaVersion = "fdr_channel/v1";
    public const string ToolName = "cdp_fdr";
    public const string GoName = "fdr";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "help" or "status" => Scene(),
            "tail" or "list" or "recent" => Tail(args),
            "stats" or "summary" => Stats(args),
            "slow" or "incidents" => Slow(args),
            _ => Fail("unknown_op", "op=scene|tail|stats|slow")
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var n = IdeFlightDataRecorder.ReadTail(1).Count;
        // Cheap pulse: prefer file length hint without full parse when empty check needed.
        try
        {
            var path = IdeFlightDataRecorder.TapePath;
            if (!File.Exists(path))
                return "fdr · empty · go=fdr";
            var len = new FileInfo(path).Length;
            return len == 0
                ? "fdr · empty · go=fdr"
                : $"fdr · tape {len}B · go=fdr op=stats";
        }
        catch
        {
            return n == 0 ? "fdr · empty · go=fdr" : "fdr · go=fdr op=stats";
        }
    }

    static object Scene() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "scene",
        go = GoName,
        tool = ToolName,
        tape = IdeFlightDataRecorder.TapePath,
        max_lines = IdeFlightDataRecorder.DefaultMaxLines,
        ops = new[] { "scene", "tail", "stats", "slow" },
        pulse = PulseLine(),
        next = new object[]
        {
            new { go = "fdr", label = "Stats", why = "op=stats — p50/p95 by tool" },
            new { go = "fdr", label = "Slow", why = "op=slow — top latency events" },
            new { go = "fdr", label = "Tail", why = "op=tail limit=40" }
        },
        hint =
            "Black-box FDR: dense tool-call tape (organ/op/latency/outcome/phase). " +
            "Not chat transcript. Use for incident analysis; VDR deferred. " +
            "Auto timeout_wake from stats = later peel."
    };

    static object Tail(IReadOnlyDictionary<string, JsonElement> args)
    {
        var limit = OptInt(args, "limit") ?? 40;
        var events = IdeFlightDataRecorder.ReadTail(limit);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "tail",
            go = GoName,
            count = events.Count,
            events = events.Select(IdeFlightDataRecorder.Slim).ToArray(),
            tape = IdeFlightDataRecorder.TapePath,
            hint = "Newest at end. Dense rows — prefer op=stats for overview."
        };
    }

    static object Stats(IReadOnlyDictionary<string, JsonElement> args)
    {
        var lookback = OptInt(args, "limit") ?? OptInt(args, "lookback") ?? 500;
        var stats = IdeFlightDataRecorder.BuildStats(lookback);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "stats",
            go = GoName,
            stats,
            hint = "p95 outliers → candidates for timeout_wake / async peel. Manual override stays."
        };
    }

    static object Slow(IReadOnlyDictionary<string, JsonElement> args)
    {
        var lookback = OptInt(args, "limit") ?? OptInt(args, "lookback") ?? 500;
        var minMs = OptInt(args, "min_ms") ?? 1000;
        var events = IdeFlightDataRecorder.ReadTail(Math.Clamp(lookback, 10, IdeFlightDataRecorder.DefaultMaxLines))
            .Where(e => e.ElapsedMs >= minMs || e.WakeExceeded || e.Outcome is "error" or "cancel")
            .OrderByDescending(e => e.ElapsedMs)
            .Take(40)
            .Select(IdeFlightDataRecorder.Slim)
            .ToArray();
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "slow",
            go = GoName,
            min_ms = minMs,
            count = events.Length,
            events,
            hint = "Incidents = slow / wake / error / cancel — not full chat."
        };
    }

    static object Fail(string reason, string hint) => new
    {
        schema = SchemaVersion,
        ok = false,
        go = GoName,
        tool = ToolName,
        reason,
        hint
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String
            && int.TryParse(el.GetString(), out var parsed))
            return parsed;
        return null;
    }
}
