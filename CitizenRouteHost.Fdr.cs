#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent fdr — sync IdeFdrChannel; place fdr organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? FdrHandleOverride { get; set; }

    static Applied RunFdr(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && FdrHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "fdr",
                Go: "fdr",
                Reason: "no_session");
        }

        var args = BuildFdrArgs(route, op);

        try
        {
            object result;
            if (FdrHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeFdrChannel.HandleJson(session!, args);

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadSoftInstrumentOk(json);
            var pulse = TryReadSoftInstrumentPulse(json, "fdr", op);
            var seat = IdeDeskSeats.PlaceOrgan("fdr");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "fdr",
                Seat: seat,
                Go: "fdr",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "fdr_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "fdr",
                Go: "fdr",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildFdrArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        foreach (var key in FdrArgKeys)
            PutIfPresent(args, key, CitizenIntentRouter.ExtractKeyedValue(raw, key));
        return args;
    }

    static readonly string[] FdrArgKeys =
    [
        "n", "limit", "kind", "since", "wake_kind", "overlay"
    ];
}
