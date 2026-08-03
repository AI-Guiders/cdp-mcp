#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent pressure — sync IdePressureChannel; place pressure organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake pressure JSON; live uses <see cref="IdePressureChannel.Handle"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? PressureHandleOverride { get; set; }

    static Applied RunPressure(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && PressureHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "pressure",
                Go: "pressure",
                Reason: "no_session");
        }

        var args = BuildPressureArgs(route.Raw, op);

        try
        {
            object result;
            if (PressureHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdePressureChannel.Handle(session!, args);

            var json = result is string s
                ? s
                : JsonSerializer.Serialize(result);
            var ok = TryReadPressureOk(json);
            var pulse = TryReadPressurePulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("pressure");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "pressure",
                Seat: seat,
                Go: "pressure",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "pressure_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "pressure",
                Go: "pressure",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildPressureArgs(string raw, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        foreach (var key in PressureStringKeys)
        {
            var val = ExtractMcpKeyed(raw, key);
            if (val is { Length: > 0 })
                args[key] = JsonSerializer.SerializeToElement(val);
        }

        if (ExtractMcpKeyed(raw, "strict") is { Length: > 0 } strictRaw
            && bool.TryParse(strictRaw, out var strict))
            args["strict"] = JsonSerializer.SerializeToElement(strict);

        if (ExtractMcpKeyed(raw, "limit") is { Length: > 0 } limitRaw
            && int.TryParse(limitRaw, out var limit))
            args["limit"] = JsonSerializer.SerializeToElement(limit);

        return args;
    }

    static readonly string[] PressureStringKeys =
    [
        "body", "text", "content", "why", "ignite", "plan", "note", "to"
    ];

    static bool TryReadPressureOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            return root.TryGetProperty("op", out _) || root.TryGetProperty("pulse", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadPressurePulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("pressure " + op + " " + pulse);

            return TruncPulse("pressure " + op);
        }
        catch
        {
            return TruncPulse("pressure " + op);
        }
    }
}
