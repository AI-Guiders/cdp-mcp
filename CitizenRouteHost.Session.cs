#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent session — sync MetaDispatch cdp_session; place session organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake session JSON; live uses MetaDispatchResolver("cdp_session", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? SessionDispatchOverride { get; set; }

    static Applied RunSession(CitizenIntentRouter.Route route)
    {
        var args = BuildSessionArgs(route);

        try
        {
            string json;
            if (SessionDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_session", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadSessionOk(json);
            var pulse = TryReadSessionPulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("session");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "session",
                Seat: seat,
                Go: "session",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "session_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "session",
                Go: "session",
                Reason: "session_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "session",
                Go: "session",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildSessionArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var includePack = string.Equals(route.Op, "include_pack", StringComparison.Ordinal)
            || CitizenIntentRouter.IsTruthyKeyed(route.Raw, "include_pack")
            || CitizenIntentRouter.IsTruthyKeyed(route.Raw, "pack");
        if (includePack)
            args["include_pack"] = JsonSerializer.SerializeToElement(true);
        return args;
    }

    static bool TryReadSessionOk(string json)
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
            return root.TryGetProperty("plane", out _)
                || root.TryGetProperty("context", out _)
                || root.TryGetProperty("shortlist", out _)
                || root.TryGetProperty("health", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadSessionPulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(ContextJsonBody(json));
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            var phase = "?";
            var obj = "?";
            if (root.TryGetProperty("context", out var ctx))
            {
                if (ctx.TryGetProperty("phase", out var ph) && ph.ValueKind == JsonValueKind.String
                    && ph.GetString() is { Length: > 0 } phaseVal)
                    phase = phaseVal;
                if (ctx.TryGetProperty("object", out var o) && o.ValueKind == JsonValueKind.String
                    && o.GetString() is { Length: > 0 } objVal)
                    obj = objVal;
            }

            var pack = "";
            if (root.TryGetProperty("pack", out var packEl))
            {
                var reason = packEl.TryGetProperty("reason", out var reasonEl)
                    && reasonEl.ValueKind == JsonValueKind.String
                        ? reasonEl.GetString()
                        : null;
                if (!string.IsNullOrWhiteSpace(reason)
                    && reason.Contains("omitted", StringComparison.OrdinalIgnoreCase))
                    pack = " · A";
                else if (packEl.TryGetProperty("available", out var av) && av.ValueKind == JsonValueKind.True)
                    pack = " · pack";
                else if (packEl.TryGetProperty("pack_id", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { Length: > 0 })
                    pack = " · pack";
            }

            return TruncPulse("session · " + phase + "/" + obj + pack);
        }
        catch
        {
            return TruncPulse("session");
        }
    }
}
