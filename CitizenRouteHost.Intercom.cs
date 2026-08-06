#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent intercom — sync IdeCideIntercomChannel; place intercom organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake intercom JSON; live uses <see cref="IdeCideIntercomChannel.HandleJson"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? IntercomHandleOverride { get; set; }

    static Applied RunIntercom(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildIntercomArgs(route, op);

        try
        {
            var json = IntercomHandleOverride is { } ov
                ? ov(args)
                : IdeCideIntercomChannel.HandleJson(args);
            var ok = TryReadIntercomOk(json);
            var pulse = TryReadIntercomPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("intercom");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "intercom",
                Seat: seat,
                Go: "intercom",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "intercom_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "intercom",
                Go: "intercom",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildIntercomArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        PutIfPresent(args, "body",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "message")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "msg"));
        PutIfPresent(args, "to",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "to")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "with"));
        PutIfPresent(args, "from", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from"));
        PutIfPresent(args, "name",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "name")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "display_name")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "as"));
        PutIfPresent(args, "kind",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "kind")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "role"));
        PutIfPresent(args, "channel",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "channel")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "feed"));
        PutIfPresent(args, "origin", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "origin"));
        PutIfPresent(args, "id", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "id"));
        PutIfPresent(args, "seat",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "seat")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "who"));
        PutIfPresent(args, "state",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "state")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "status")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "presence"));
        PutIntIfPresent(args, "limit", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "limit")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "take")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "n"));
        PutIntIfPresent(args, "ttl_s",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "ttl_s")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "ttl"));

        return args;
    }

    static bool TryReadIntercomOk(string json)
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

    static string? TryReadIntercomPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("intercom " + op + " " + pulse);

            var bits = new List<string> { "intercom", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("chat", out var chat) && chat.ValueKind == JsonValueKind.String
                && chat.GetString() is { Length: > 0 } c)
                bits.Add(c);
            else if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("intercom " + op);
        }
    }
}
