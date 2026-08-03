#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent health — sync MetaDispatch cdp_health; place health organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake health JSON; live uses MetaDispatchResolver("cdp_health", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? HealthDispatchOverride { get; set; }

    static Applied RunHealth(CitizenIntentRouter.Route route)
    {
        var args = BuildHealthArgs(route);

        try
        {
            string json;
            if (HealthDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_health", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadHealthOk(json);
            var pulse = TryReadHealthPulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("health");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "health",
                Seat: seat,
                Go: "health",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "health_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "health",
                Go: "health",
                Reason: "health_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "health",
                Go: "health",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildHealthArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        PutIfPresent(args, "explain_tool", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "explain_tool")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "explain")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "tool"));
        return args;
    }

    static bool TryReadHealthOk(string json)
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
            return root.TryGetProperty("runtime", out _)
                || root.TryGetProperty("ops_pulse", out _)
                || root.TryGetProperty("seats", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadHealthPulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ops_pulse", out var ops) && ops.ValueKind == JsonValueKind.String
                && ops.GetString() is { Length: > 0 } opsPulse)
                return TruncPulse(opsPulse);

            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("health · " + e);

            var ver = "?";
            if (root.TryGetProperty("runtime", out var rt)
                && rt.TryGetProperty("version", out var v)
                && v.ValueKind == JsonValueKind.String
                && v.GetString() is { Length: > 0 } version)
                ver = version;

            var lag = "";
            if (root.TryGetProperty("seats", out var seats)
                && seats.TryGetProperty("lag", out var lagEl))
            {
                if (lagEl.ValueKind == JsonValueKind.True)
                    lag = " · lag";
                else if (lagEl.ValueKind == JsonValueKind.False)
                    lag = " · clear";
            }

            return TruncPulse("health · " + ver + lag);
        }
        catch
        {
            return null;
        }
    }
}
