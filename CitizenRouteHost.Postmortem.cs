#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent postmortem — sync IdePostmortemChannel; place postmortem organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? PostmortemHandleOverride { get; set; }

    static Applied RunPostmortem(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && PostmortemHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "postmortem",
                Go: "postmortem",
                Reason: "no_session");
        }

        var args = BuildPostmortemArgs(route, op);

        try
        {
            object result;
            if (PostmortemHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdePostmortemChannel.HandleJson(session!, args);

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadSoftInstrumentOk(json);
            var pulse = TryReadSoftInstrumentPulse(json, "postmortem", op);
            var seat = IdeDeskSeats.PlaceOrgan("postmortem");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "postmortem",
                Seat: seat,
                Go: "postmortem",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "postmortem_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "postmortem",
                Go: "postmortem",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildPostmortemArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        foreach (var key in PostmortemArgKeys)
            PutIfPresent(args, key, CitizenIntentRouter.ExtractKeyedValue(raw, key));
        return args;
    }

    static readonly string[] PostmortemArgKeys =
    [
        "happened", "system_root", "why_repeated", "fix", "do_not", "call_id", "n", "limit"
    ];
}
