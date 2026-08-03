#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent cockpit — sync MetaDispatch cdp_cockpit; place cockpit organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake cockpit JSON; live uses MetaDispatchResolver("cdp_cockpit", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? CockpitDispatchOverride { get; set; }

    static Applied RunCockpit(CitizenIntentRouter.Route route)
    {
        var args = BuildCockpitArgs(route);

        try
        {
            string json;
            if (CockpitDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_cockpit", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadCockpitOk(json);
            var pulse = TryReadCockpitPulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("cockpit");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "cockpit",
                Seat: seat,
                Go: "cockpit",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "cockpit_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "cockpit",
                Go: "cockpit",
                Reason: "cockpit_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "cockpit",
                Go: "cockpit",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildCockpitArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        PutIfPresent(args, "layout", route.Scene
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "layout"));
        PutIfPresent(args, "pane_full", route.Organ
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "pane_full")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "full_pane"));
        PutIfPresent(args, "go_detail", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "go_detail"));
        PutIfPresent(args, "desk_detail", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "desk_detail")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "nav_detail"));
        PutIfPresent(args, "locus", route.Cmd
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "locus")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "focus"));
        return args;
    }

    static bool TryReadCockpitOk(string json)
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
            return root.TryGetProperty("schema", out _)
                || root.TryGetProperty("seats", out _)
                || root.TryGetProperty("view", out _)
                || root.TryGetProperty("role", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadCockpitPulse(string json)
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
                return TruncPulse("cockpit · " + e);

            var seats = "?";
            if (root.TryGetProperty("seats", out var seatsEl))
            {
                if (seatsEl.TryGetProperty("count", out var c)
                    && c.ValueKind == JsonValueKind.Number
                    && c.TryGetInt32(out var ci))
                    seats = ci.ToString();
                else if (seatsEl.TryGetProperty("slots", out var slots)
                    && slots.ValueKind == JsonValueKind.Array)
                    seats = slots.GetArrayLength().ToString();
            }

            var mode = "?";
            if (root.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String
                && modeEl.GetString() is { Length: > 0 } modeVal)
                mode = modeVal;

            var alert = "";
            if (root.TryGetProperty("alert", out var alertEl)
                && alertEl.TryGetProperty("pulse", out var ap)
                && ap.ValueKind == JsonValueKind.String
                && ap.GetString() is { Length: > 0 } apVal)
                alert = " · " + apVal;

            return TruncPulse("cockpit · mode=" + mode + " · seats=" + seats + alert);
        }
        catch
        {
            return TruncPulse("cockpit");
        }
    }
}
