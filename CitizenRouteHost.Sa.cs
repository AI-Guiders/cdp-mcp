#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent sa — sync MetaDispatch cdp_sa; place sa_desk organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake sa JSON; live uses MetaDispatchResolver("cdp_sa", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? SaDispatchOverride { get; set; }

    static Applied RunSa(CitizenIntentRouter.Route route)
    {
        var args = BuildSaArgs(route);

        try
        {
            string json;
            if (SaDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_sa", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadSaOk(json);
            var pulse = TryReadSaPulse(json, route.Op ?? "slim");
            var seat = IdeDeskSeats.PlaceOrgan("sa_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "sa",
                Seat: seat,
                Go: "sa_desk",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "sa_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "sa",
                Go: "sa_desk",
                Reason: "sa_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "sa",
                Go: "sa_desk",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildSaArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var depth = route.Op
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "depth")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "shape")
            ?? "pulse";
        args["depth"] = JsonSerializer.SerializeToElement(depth.Trim().ToLowerInvariant());

        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "locus")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "focus"));
        PutIfPresent(args, "scope", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scope"));
        return args;
    }

    static bool TryReadSaOk(string json)
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
            return root.TryGetProperty("pulse", out _) || root.TryGetProperty("verdict", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadSaPulse(string json, string depth)
    {
        try
        {
            using var doc = JsonDocument.Parse(ContextJsonBody(json));
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("verdict", out var v) && v.ValueKind == JsonValueKind.String
                && v.GetString() is { Length: > 0 } verdict)
                return TruncPulse("sa_desk · " + verdict + " · depth=" + depth);

            return TruncPulse("sa_desk · depth=" + depth);
        }
        catch
        {
            return TruncPulse("sa_desk · depth=" + depth);
        }
    }
}
