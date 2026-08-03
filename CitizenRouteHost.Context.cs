#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent context — sync MetaDispatch cdp_context; place context organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake context JSON; live uses MetaDispatchResolver("cdp_context", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? ContextDispatchOverride { get; set; }

    static Applied RunContext(CitizenIntentRouter.Route route)
    {
        var args = BuildContextArgs(route);

        try
        {
            string json;
            if (ContextDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_context", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadContextOk(json);
            var pulse = TryReadContextPulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("context");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "context",
                Seat: seat,
                Go: "context",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "context_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "context",
                Go: "context",
                Reason: "context_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "context",
                Go: "context",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildContextArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        PutIfPresent(args, "phase", route.Scene
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "phase"));
        PutIfPresent(args, "object", route.Organ
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "object")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "obj"));
        PutIfPresent(args, "intent", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "intent"));
        PutIfPresent(args, "language", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "language")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "lang"));

        if (string.Equals(route.Op, "get", StringComparison.OrdinalIgnoreCase)
            || CitizenIntentRouter.IsTruthyKeyed(route.Raw, "get"))
            args["get"] = JsonSerializer.SerializeToElement(true);

        if (string.Equals(route.Cmd, "layout_hold", StringComparison.OrdinalIgnoreCase)
            || CitizenIntentRouter.IsTruthyKeyed(route.Raw, "layout_hold")
            || CitizenIntentRouter.IsTruthyKeyed(route.Raw, "hold"))
            args["layout_hold"] = JsonSerializer.SerializeToElement(true);

        return args;
    }

    static bool TryReadContextOk(string json)
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
            return root.TryGetProperty("phase", out _)
                || root.TryGetProperty("object", out _)
                || root.TryGetProperty("context", out _)
                || root.TryGetProperty("pulse", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadContextPulse(string json)
    {
        try
        {
            var body = ContextJsonBody(json);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("context · " + e);

            var phase = "?";
            var obj = "?";
            if (root.TryGetProperty("phase", out var ph) && ph.ValueKind == JsonValueKind.String
                && ph.GetString() is { Length: > 0 } phaseVal)
                phase = phaseVal;
            else if (root.TryGetProperty("context", out var ctx)
                && ctx.TryGetProperty("phase", out var cph)
                && cph.ValueKind == JsonValueKind.String
                && cph.GetString() is { Length: > 0 } cPhase)
                phase = cPhase;

            if (root.TryGetProperty("object", out var ob) && ob.ValueKind == JsonValueKind.String
                && ob.GetString() is { Length: > 0 } objVal)
                obj = objVal;
            else if (root.TryGetProperty("context", out var ctx2)
                && ctx2.TryGetProperty("object", out var cob)
                && cob.ValueKind == JsonValueKind.String
                && cob.GetString() is { Length: > 0 } cObj)
                obj = cObj;

            var tail = "";
            if (json.Contains("desk_layout: held", StringComparison.Ordinal))
                tail = " · held";
            else if (json.Contains("list_changed", StringComparison.Ordinal))
                tail = " · list_changed";

            return TruncPulse("context · " + phase + "/" + obj + tail);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Meta cdp_context appends <c>\n# list_changed</c> / desk_layout tails after JSON.</summary>
    static string ContextJsonBody(string raw)
    {
        var i = raw.IndexOf("\n#", StringComparison.Ordinal);
        return i < 0 ? raw : raw[..i];
    }
}
