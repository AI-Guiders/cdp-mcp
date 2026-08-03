#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent domain — sync IdeDomainChannel; place domain organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake domain JSON; live uses <see cref="IdeDomainChannel.HandleJson"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? DomainHandleOverride { get; set; }

    static Applied RunDomain(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && DomainHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "domain",
                Go: "domain",
                Reason: "no_session");
        }

        var args = BuildDomainArgs(route, op);

        try
        {
            object result;
            if (DomainHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeDomainChannel.HandleJson(session!, args);

            var json = result is string s
                ? s
                : JsonSerializer.Serialize(result);
            var ok = TryReadDomainOk(json);
            var pulse = TryReadDomainPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("domain");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "domain",
                Seat: seat,
                Go: "domain",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "domain_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "domain",
                Go: "domain",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildDomainArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        var keyedId = CitizenIntentRouter.ExtractKeyedValue(raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "card")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "name");
        var keyedFocus = CitizenIntentRouter.ExtractKeyedValue(raw, "focus")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "hint")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "q");

        if (op is "card")
            PutIfPresent(args, "id", keyedId ?? route.Path);
        else if (op is "pulse" or "scene")
        {
            PutIfPresent(args, "focus", keyedFocus ?? (op == "pulse" ? route.Path : null));
            PutIfPresent(args, "id", keyedId);
        }
        else
        {
            PutIfPresent(args, "id", keyedId ?? route.Path);
            PutIfPresent(args, "focus", keyedFocus);
        }

        return args;
    }

    static bool TryReadDomainOk(string json)
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
            return root.TryGetProperty("pulse", out _) || root.TryGetProperty("cards", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadDomainPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("domain " + op + " " + pulse);

            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                && idEl.GetString() is { Length: > 0 } id)
                return TruncPulse("domain " + op + " " + id);

            return TruncPulse("domain " + op);
        }
        catch
        {
            return TruncPulse("domain " + op);
        }
    }
}
