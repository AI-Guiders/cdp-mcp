#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent ignite — sync IdeIgniteChannel (arm/list/continuity); place ignite organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake ignite JSON; live uses <see cref="IdeIgniteChannel.Handle"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? IgniteHandleOverride { get; set; }

    static Applied RunIgnite(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "continuity" : route.Op!;
        var args = BuildIgniteArgs(route.Raw, op);

        try
        {
            object result;
            if (IgniteHandleOverride is { } ov)
                result = ov(args);
            else
                result = IdeIgniteChannel.Handle(args);

            var json = result is string s
                ? s
                : JsonSerializer.Serialize(result);
            var ok = TryReadIgniteOk(json, op);
            var pulse = TryReadIgnitePulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("ignite");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "ignite",
                Seat: seat,
                Go: "ignite",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "ignite_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "ignite",
                Go: "ignite",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildIgniteArgs(string raw, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        foreach (var key in IgniteStringKeys)
        {
            var val = ExtractMcpKeyed(raw, key);
            if (val is { Length: > 0 })
                args[key == "event" ? "when" : key] = JsonSerializer.SerializeToElement(val);
        }

        // Alias event= → when=
        if (!args.ContainsKey("when") && ExtractMcpKeyed(raw, "event") is { Length: > 0 } ev)
            args["when"] = JsonSerializer.SerializeToElement(ev);

        PutIgniteBool(args, raw, "last_once");
        PutIgniteBool(args, raw, "force");
        PutIgniteBool(args, raw, "all");
        PutIgniteBool(args, raw, "ok_only");
        PutIgniteBool(args, raw, "armed");

        if (ExtractMcpKeyed(raw, "settle_seconds") is { Length: > 0 } settleRaw
            && int.TryParse(settleRaw, out var settle))
            args["settle_seconds"] = JsonSerializer.SerializeToElement(settle);

        if (ExtractMcpKeyed(raw, "port") is { Length: > 0 } portRaw
            && int.TryParse(portRaw, out var port))
            args["port"] = JsonSerializer.SerializeToElement(port);

        return args;
    }

    static void PutIgniteBool(Dictionary<string, JsonElement> args, string raw, string key)
    {
        if (ExtractMcpKeyed(raw, key) is { Length: > 0 } v
            && bool.TryParse(v, out var b))
            args[key] = JsonSerializer.SerializeToElement(b);
    }

    static readonly string[] IgniteStringKeys =
    [
        "when", "event", "in", "task", "id", "charge", "chat", "message"
    ];

    static bool TryReadIgniteOk(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            // Arm/list often return schema without ok=false on success.
            if (root.TryGetProperty("op", out _) || root.TryGetProperty("arm", out _)
                || root.TryGetProperty("arms", out _) || root.TryGetProperty("pulse", out _))
                return true;
            return op is "continuity" or "list" or "resume";
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadIgnitePulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("ignite " + op + " " + pulse);

            if (root.TryGetProperty("arm", out var arm) && arm.ValueKind == JsonValueKind.Object
                && arm.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                && idEl.GetString() is { Length: > 0 } id)
                return TruncPulse("ignite " + op + " " + id);

            return TruncPulse("ignite " + op);
        }
        catch
        {
            return TruncPulse("ignite " + op);
        }
    }
}
