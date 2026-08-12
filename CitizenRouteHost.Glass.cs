#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent glass|surface_desk — sync IdeGlassSurfaceChannel; place surface_desk organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? GlassHandleOverride { get; set; }

    static Applied RunGlass(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && GlassHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "glass",
                Go: "surface_desk",
                Reason: "no_session");
        }

        var args = BuildGlassArgs(route, op);

        try
        {
            object result;
            if (GlassHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeGlassSurfaceChannel.HandleJson(session!, args);

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadSoftInstrumentOk(json);
            var pulse = TryReadSoftInstrumentPulse(json, "glass", op);
            var seat = IdeDeskSeats.PlaceOrgan("surface_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "glass",
                Seat: seat,
                Go: "surface_desk",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "glass_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "glass",
                Go: "surface_desk",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildGlassArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        foreach (var key in GlassArgKeys)
            PutIfPresent(args, key, CitizenIntentRouter.ExtractKeyedValue(raw, key));
        return args;
    }

    static readonly string[] GlassArgKeys =
    [
        "target", "selector", "text", "keys", "action", "layout", "appearance",
        "color", "width", "height", "timeout_ms", "confirm", "message", "title"
    ];
}
