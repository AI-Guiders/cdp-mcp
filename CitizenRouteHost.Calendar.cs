#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent calendar — sync IdeCalendarChannel; place calendar organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake calendar JSON; live uses <see cref="IdeCalendarChannel.Handle"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? CalendarHandleOverride { get; set; }

    static Applied RunCalendar(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && CalendarHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "calendar",
                Go: "calendar",
                Reason: "no_session");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        try
        {
            object result;
            if (CalendarHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeCalendarChannel.Handle(session!, args);

            var json = result is string s
                ? s
                : JsonSerializer.Serialize(result);
            var ok = TryReadCalendarOk(json);
            var pulse = TryReadCalendarPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("calendar");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "calendar",
                Seat: seat,
                Go: "calendar",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "calendar_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "calendar",
                Go: "calendar",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static bool TryReadCalendarOk(string json)
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

    static string? TryReadCalendarPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("calendar " + op + " " + pulse);

            return TruncPulse("calendar " + op);
        }
        catch
        {
            return TruncPulse("calendar " + op);
        }
    }
}
