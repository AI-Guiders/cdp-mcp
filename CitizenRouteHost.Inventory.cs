#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent inventory — sync IdeInventoryChannel; place inventory organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? InventoryHandleOverride { get; set; }

    static Applied RunInventory(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && InventoryHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "inventory",
                Go: "inventory",
                Reason: "no_session");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        try
        {
            object result = InventoryHandleOverride is { } ov
                ? ov(session ?? new SessionContext(), args)
                : IdeInventoryChannel.HandleJson(session!, args);
            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = !json.Contains("\"ok\": false", StringComparison.OrdinalIgnoreCase);
            var seat = IdeDeskSeats.PlaceOrgan("inventory");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "inventory",
                Seat: seat,
                Go: "inventory",
                Pulse: TruncPulse("inventory " + op),
                Reason: ok ? null : "inventory_failed");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "inventory",
                Go: "inventory",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }
}
