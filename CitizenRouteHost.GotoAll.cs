#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent cdp_goto|goto_all — sync MetaDispatch cdp_goto; place goto organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake goto JSON; live uses MetaDispatchResolver("cdp_goto", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? GotoAllDispatchOverride { get; set; }

    static Applied RunGotoAll(CitizenIntentRouter.Route route)
    {
        var args = BuildGotoAllArgs(route);

        try
        {
            string json;
            if (GotoAllDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_goto", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadGotoAllOk(json);
            var pulse = TryReadGotoAllPulse(json, route.Tool);
            var seat = IdeDeskSeats.PlaceOrgan("goto");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "goto",
                Seat: seat,
                Go: "goto",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "goto_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "goto",
                Go: "goto",
                Reason: "goto_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "goto",
                Go: "goto",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildGotoAllArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var raw = route.Raw;

        PutIfPresent(args, "query", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "query")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "q")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "text"));
        PutIfPresent(args, "kind", route.Op
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "kind")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "filter"));
        PutBoolIfPresent(args, "peek", route.NewString
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "peek"));

        var max = route.Detail ?? CitizenIntentRouter.ExtractKeyedValue(raw, "max");
        if (max is { Length: > 0 } && int.TryParse(max, out var n))
            args["max"] = JsonSerializer.SerializeToElement(n);

        return args;
    }

    static bool TryReadGotoAllOk(string json)
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
            return root.TryGetProperty("hits", out _)
                || root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("schema", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadGotoAllPulse(string json, string? query)
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
                return TruncPulse("goto · " + e);

            var count = 0;
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                count = n;
            else if (root.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
                count = hits.GetArrayLength();

            var q = string.IsNullOrWhiteSpace(query) ? "" : " " + query.Trim();
            return TruncPulse("goto · " + count + " hit(s)" + q);
        }
        catch
        {
            return null;
        }
    }
}
