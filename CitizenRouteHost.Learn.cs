#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent learn — sync MetaDispatch cdp_learn; place learn organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake learn JSON; live uses MetaDispatchResolver("cdp_learn", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? LearnDispatchOverride { get; set; }

    static Applied RunLearn(CitizenIntentRouter.Route route)
    {
        var args = BuildLearnArgs(route);

        try
        {
            string json;
            if (LearnDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_learn", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLearnOk(json);
            var pulse = TryReadLearnPulse(json, route.Op ?? "scene");
            var seat = IdeDeskSeats.PlaceOrgan("learn");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "learn",
                Seat: seat,
                Go: "learn",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "learn_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "learn",
                Go: "learn",
                Reason: "learn_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "learn",
                Go: "learn",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildLearnArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = route.Op
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "op")
            ?? "scene";
        args["op"] = JsonSerializer.SerializeToElement(op.Trim().ToLowerInvariant());

        PutIfPresent(args, "title", route.Scene
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "title")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "name"));
        PutIfPresent(args, "id", route.Organ
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "id"));
        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "file_path"));
        PutIfPresent(args, "topic", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "topic"));
        PutIfPresent(args, "tags", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "tags"));
        PutIfPresent(args, "body", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "learning"));
        PutIfPresent(args, "limit", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "limit"));
        return args;
    }

    static bool TryReadLearnOk(string json)
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
            return root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("journal", out _)
                || root.TryGetProperty("entries", out _)
                || root.TryGetProperty("entry", out _)
                || root.TryGetProperty("id", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadLearnPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(ContextJsonBody(json));
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("learn · " + e);

            if (root.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number)
                return TruncPulse("learn · " + op + " · n=" + c.GetInt32());

            if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } idv)
                return TruncPulse("learn · " + op + " · id=" + idv);

            return TruncPulse("learn · " + op);
        }
        catch
        {
            return TruncPulse("learn · " + op);
        }
    }
}
