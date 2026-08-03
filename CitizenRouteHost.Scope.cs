#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent project_switch — sync IdeScopeChannel; place project_switch organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? ScopeHandleOverride { get; set; }

    static Applied RunScope(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && ScopeHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "project_switch",
                Go: "project_switch",
                Reason: "no_session");
        }

        var args = BuildScopeArgs(route, op);

        try
        {
            object result;
            if (ScopeHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeScopeChannel.HandleJson(session!, args);

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadSoftOrganOk(json);
            var pulse = TryReadSoftOrganPulse(json, "ps", op);
            var seat = IdeDeskSeats.PlaceOrgan("project_switch");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "project_switch",
                Seat: seat,
                Go: "project_switch",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "scope_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "project_switch",
                Go: "project_switch",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildScopeArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        foreach (var key in ScopeArgKeys)
            PutIfPresent(args, key, CitizenIntentRouter.ExtractKeyedValue(raw, key));
        return args;
    }

    static readonly string[] ScopeArgKeys =
    [
        "primary", "scope", "text", "body", "markers"
    ];
}
