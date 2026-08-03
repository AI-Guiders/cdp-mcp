#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent elicit — MetaDispatch cdp_elicit; no desk PlaceOrgan (spike Meta, no go pin).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake elicit JSON; live uses MetaDispatchResolver("cdp_elicit", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? ElicitDispatchOverride { get; set; }

    static Applied RunElicit(CitizenIntentRouter.Route route)
    {
        var args = BuildElicitArgs(route);

        try
        {
            string json;
            if (ElicitDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_elicit", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadElicitOk(json);
            var pulse = TryReadElicitPulse(json, route.Op ?? "peek");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "elicit",
                Go: "elicit",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "elicit_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "elicit",
                Go: "elicit",
                Reason: "elicit_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "elicit",
                Go: "elicit",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildElicitArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = route.Op
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "op")
            ?? "peek";
        args["op"] = JsonSerializer.SerializeToElement(op.Trim().ToLowerInvariant());

        PutIfPresent(args, "message", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "message")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "ask"));
        return args;
    }

    static bool TryReadElicitOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(ContextJsonBody(json));
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            return root.TryGetProperty("elicitation", out _)
                || root.TryGetProperty("hint", out _)
                || root.TryGetProperty("pulse", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadElicitPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(ContextJsonBody(json));
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String
                && h.GetString() is { Length: > 0 } hint)
                return TruncPulse("elicit · " + hint);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("elicit · " + e);

            return TruncPulse("elicit · " + op);
        }
        catch
        {
            return TruncPulse("elicit · " + op);
        }
    }
}
