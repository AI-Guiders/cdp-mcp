#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=fdr</c> / Meta <c>cdp_fdr</c> — Black-box FDR desk (incident tape, not chat).
/// Ops: scene|tail|stats|slow|open|trace|suggest|apply|clear_overlay. VDR (cabin voice) deferred.
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
            "open" or "inflight" or "ghost" => Open(args),
            "trace" or "flight" or "call" => Trace(args),
            "suggest" or "thresholds" or "timeout_wake" => Suggest(args),
            "apply" => Apply(args),
            "clear_overlay" or "clear" => IdeFdrThresholdPolicy.ClearOverlay(),
            _ => Fail("unknown_op", "op=scene|tail|stats|slow|open|trace|suggest|apply|clear_overlay")
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var n = IdeFlightDataRecorder.ReadTail(1).Count;
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
        ops = new[] { "scene", "tail", "stats", "slow", "open", "trace", "suggest", "apply", "clear_overlay" },
        pulse = PulseLine(),
        next = new object[]
        {
            new { go = "fdr", label = "Stats", why = "op=stats — p50/p95 + timeout_wake candidates" },
            new { go = "fdr", label = "Open", why = "op=open — tool_start without close (ghost hang dig)" },
            new { go = "fdr", label = "Trace", why = "op=trace call= — start+ticks+close dynamics" },
            new { go = "fdr", label = "Suggest", why = "op=suggest — raise|hang|async from tape" },
            new { go = "fdr", label = "Apply", why = "op=apply — arm overlay from raise candidates" },
            new { go = "fdr", label = "Slow", why = "op=slow — top latency events" },
            new { go = "fdr", label = "Tail", why = "op=tail limit=40" }
        },
        hint =
            "Black-box FDR: tool_start → tool_tick (dynamics) → closed tool_call. " +
            "Ghost hang dig: op=open / op=trace call=. " +
            "timeout_wake: op=suggest → op=apply overlay (evidence, not hand guess)."
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
            hint = "timeout_wake block = FDR-derived candidates. op=suggest detail; op=apply to arm overlay."
        };
    }

    static object Suggest(IReadOnlyDictionary<string, JsonElement> args)
    {
        var lookback = OptInt(args, "limit") ?? OptInt(args, "lookback") ?? 500;
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "suggest",
            go = GoName,
            timeout_wake = IdeFdrThresholdPolicy.SuggestPayload(lookback)
        };
    }

    static object Apply(IReadOnlyDictionary<string, JsonElement> args)
    {
        var lookback = OptInt(args, "limit") ?? OptInt(args, "lookback") ?? 500;
        var dry = OptBool(args, "dry_run") ?? OptBool(args, "dry") ?? false;
        return IdeFdrThresholdPolicy.Apply(lookback, dry);
    }

    static object Open(IReadOnlyDictionary<string, JsonElement> args)
    {
        var lookback = OptInt(args, "limit") ?? OptInt(args, "lookback") ?? 500;
        var open = IdeFlightDataRecorder.ListOpenFlights(lookback);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "open",
            go = GoName,
            lookback,
            count = open.Count,
            open,
            hint =
                "tool_start without later closed tool_call for same call_id. " +
                "last_tick_ms / ticks = mid-flight dynamics when present. " +
                "Host abort / hung ScriptHost without finally → ghost dig here; op=trace call= for full trail."
        };
    }

    static object Trace(IReadOnlyDictionary<string, JsonElement> args)
    {
        var call = Opt(args, "call") ?? Opt(args, "call_id") ?? Opt(args, "id") ?? "";
        var lookback = OptInt(args, "limit") ?? OptInt(args, "lookback") ?? 500;
        var trace = IdeFlightDataRecorder.TraceFlight(call, lookback);
        return new
        {
            schema = SchemaVersion,
            go = GoName,
            op = "trace",
            result = trace
        };
    }

    static object Slow(IReadOnlyDictionary<string, JsonElement> args)
    {
        var lookback = OptInt(args, "limit") ?? OptInt(args, "lookback") ?? 500;
        var minMs = OptInt(args, "min_ms") ?? 1000;
        var events = IdeFlightDataRecorder.ReadTail(Math.Clamp(lookback, 10, IdeFlightDataRecorder.DefaultMaxLines))
            .Where(IdeFlightDataRecorder.IsClosedToolCall)
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
            hint = "Incidents = slow / wake / error / cancel closed flights — not tool_start/tool_tick."
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

    static bool? OptBool(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => el.GetString()?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on",
            _ => null
        };
    }
}
