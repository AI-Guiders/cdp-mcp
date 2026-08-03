#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent qrh — sync IdeQrhChannel; place qrh organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake qrh JSON; live uses <see cref="IdeQrhChannel.HandleJson"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? QrhHandleOverride { get; set; }

    static Applied RunQrh(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "index" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && QrhHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "qrh",
                Go: "qrh",
                Reason: "no_session");
        }

        var args = BuildQrhArgs(route, op);

        try
        {
            object result;
            if (QrhHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeQrhChannel.HandleJson(session!, args);

            var json = result is string s
                ? s
                : JsonSerializer.Serialize(result);
            var ok = TryReadQrhOk(json);
            var pulse = TryReadQrhPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("qrh");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "qrh",
                Seat: seat,
                Go: "qrh",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "qrh_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "qrh",
                Go: "qrh",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildQrhArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        var keyedId = CitizenIntentRouter.ExtractKeyedValue(raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "page")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "name")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "from");
        var keyedQ = CitizenIntentRouter.ExtractKeyedValue(raw, "q")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "query");
        var keyedShelf = CitizenIntentRouter.ExtractKeyedValue(raw, "shelf")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "section");

        if (op is "open" or "related" or "remove" or "enable" or "disable")
            PutIfPresent(args, "id", keyedId ?? route.Path);
        else if (op is "search")
            PutIfPresent(args, "q", keyedQ ?? keyedId ?? route.Path);
        else if (op is "shelf")
            PutIfPresent(args, "shelf", keyedShelf ?? keyedId ?? route.Path);
        else
        {
            PutIfPresent(args, "id", keyedId ?? route.Path);
            PutIfPresent(args, "q", keyedQ);
            PutIfPresent(args, "shelf", keyedShelf);
        }

        // Overlay / add extras — pass through when present.
        foreach (var key in QrhExtraKeys)
        {
            var val = CitizenIntentRouter.ExtractKeyedValue(raw, key);
            if (val is { Length: > 0 })
                PutIfPresent(args, key, val);
        }

        return args;
    }

    static readonly string[] QrhExtraKeys =
    [
        "title", "memory", "steps", "related", "tags", "shelf", "body", "text", "content"
    ];

    static bool TryReadQrhOk(string json)
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
            return root.TryGetProperty("pulse", out _) || root.TryGetProperty("mode", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadQrhPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("qrh " + op + " " + pulse);

            return TruncPulse("qrh " + op);
        }
        catch
        {
            return TruncPulse("qrh " + op);
        }
    }
}
