#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent onboard — sync IdeOnboardChannel.HandleJson; place onboard_desk organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake onboard JSON; live uses <see cref="IdeOnboardChannel.HandleJson"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, string>? OnboardHandleOverride { get; set; }

    static Applied RunOnboard(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && OnboardHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "onboard",
                Go: "onboard_desk",
                Reason: "no_session");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        try
        {
            string json;
            if (OnboardHandleOverride is { } ov)
                json = ov(session ?? new SessionContext(), args);
            else
                json = IdeOnboardChannel.HandleJson(session!, args);

            var ok = TryReadOnboardOk(json);
            var pulse = TryReadOnboardPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("onboard_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "onboard",
                Seat: seat,
                Go: "onboard_desk",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "onboard_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "onboard",
                Go: "onboard_desk",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static bool TryReadOnboardOk(string json)
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
            return root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("entrypoints", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadOnboardPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("onboard " + op + " · " + e);

            return TruncPulse("onboard · " + op);
        }
        catch
        {
            return null;
        }
    }
}
