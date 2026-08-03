#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent teeth — sync IdeTeethChannel; place teeth organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? TeethHandleOverride { get; set; }

    static Applied RunTeeth(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && TeethHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "teeth",
                Go: "teeth",
                Reason: "no_session");
        }

        var args = BuildTeethArgs(route, op);

        try
        {
            object result;
            if (TeethHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeTeethChannel.HandleJson(session!, args);

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadSoftOrganOk(json);
            var pulse = TryReadSoftOrganPulse(json, "teeth", op);
            var seat = IdeDeskSeats.PlaceOrgan("teeth");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "teeth",
                Seat: seat,
                Go: "teeth",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "teeth_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "teeth",
                Go: "teeth",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildTeethArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "n", CitizenIntentRouter.ExtractKeyedValue(raw, "n")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "limit"));
        PutIfPresent(args, "kind", CitizenIntentRouter.ExtractKeyedValue(raw, "kind"));
        PutIfPresent(args, "id", CitizenIntentRouter.ExtractKeyedValue(raw, "id"));
        return args;
    }
}
