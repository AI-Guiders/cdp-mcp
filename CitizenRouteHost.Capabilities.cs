#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent capabilities — sync MetaDispatch cdp_capabilities; place capabilities organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake capabilities JSON; live uses MetaDispatchResolver("cdp_capabilities", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? CapabilitiesDispatchOverride { get; set; }

    static Applied RunCapabilities(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        try
        {
            string json;
            if (CapabilitiesDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_capabilities", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadCapabilitiesOk(json);
            var pulse = TryReadCapabilitiesPulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("capabilities");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "capabilities",
                Seat: seat,
                Go: "capabilities",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "capabilities_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "capabilities",
                Go: "capabilities",
                Reason: "capabilities_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "capabilities",
                Go: "capabilities",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static bool TryReadCapabilitiesOk(string json)
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
            return root.TryGetProperty("domains", out _)
                || root.TryGetProperty("affordances", out _)
                || root.TryGetProperty("layers", out _)
                || root.TryGetProperty("catalog", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadCapabilitiesPulse(string json)
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
                return TruncPulse("capabilities · " + e);

            var domains = "?";
            if (root.TryGetProperty("domains", out var domainsEl) && domainsEl.ValueKind == JsonValueKind.Array)
                domains = domainsEl.GetArrayLength().ToString();

            var afford = "?";
            if (root.TryGetProperty("affordances", out var affEl))
            {
                if (affEl.ValueKind == JsonValueKind.Number && affEl.TryGetInt32(out var ai))
                    afford = ai.ToString();
                else if (affEl.ValueKind == JsonValueKind.String && affEl.GetString() is { Length: > 0 } as_)
                    afford = as_;
            }

            var list = "";
            if (root.TryGetProperty("list_tools_count", out var listEl)
                && listEl.ValueKind == JsonValueKind.Number
                && listEl.TryGetInt32(out var li))
                list = " · list=" + li;

            return TruncPulse("capabilities · domains=" + domains + " · aff=" + afford + list);
        }
        catch
        {
            return TruncPulse("capabilities");
        }
    }
}
