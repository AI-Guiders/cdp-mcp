#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>Citizen @intent crm — sync IdeCrmChannel; place crm organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake crm JSON; live uses <see cref="IdeCrmChannel.HandleJson"/>.</summary>
    internal static Func<SessionContext, IntentWorkspaceStore?, IntentWorkspaceState?, IReadOnlyDictionary<string, JsonElement>, object>? CrmHandleOverride { get; set; }

    static Applied RunCrm(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && CrmHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "crm",
                Go: "crm",
                Reason: "no_session");
        }

        var args = BuildCrmArgs(route, op);
        IdeStageCycle.TryWorkspace(out var store, out var state, out _);

        try
        {
            object result;
            if (CrmHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), store, state, args);
            else
                result = IdeCrmChannel.HandleJson(session!, store, state, args);

            var json = result is string s
                ? s
                : JsonSerializer.Serialize(result);
            var ok = TryReadCrmOk(json);
            var pulse = TryReadCrmPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("crm");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "crm",
                Seat: seat,
                Go: "crm",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "crm_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "crm",
                Go: "crm",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildCrmArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        foreach (var key in CrmArgKeys)
            PutIfPresent(args, key, CitizenIntentRouter.ExtractKeyedValue(raw, key));

        if (route.Path is { Length: > 0 } path)
        {
            if (op is "respond" && !args.ContainsKey("code") && !args.ContainsKey("callout")
                && !args.ContainsKey("response") && !args.ContainsKey("say"))
                PutIfPresent(args, "code", path);
            else if (op is "call" && !args.ContainsKey("ask") && !args.ContainsKey("what")
                && !args.ContainsKey("text"))
                PutIfPresent(args, "ask", path);
        }

        return args;
    }

    static readonly string[] CrmArgKeys =
    [
        "ask", "what", "text", "kind", "ref", "ref_id", "plan_id",
        "code", "callout", "response", "say", "why"
    ];

    static bool TryReadCrmOk(string json)
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
            return root.TryGetProperty("pulse", out _) || root.TryGetProperty("lexicon", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadCrmPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("crm " + op + " " + pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("crm " + op + " · " + e);

            return TruncPulse("crm " + op);
        }
        catch
        {
            return TruncPulse("crm " + op);
        }
    }
}
