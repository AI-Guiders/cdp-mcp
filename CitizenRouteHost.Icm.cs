#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent icm — sync IdeIcmChannel.HandleJsonAsync; place icm_desk organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake icm JSON; live uses <see cref="IdeIcmChannel.HandleJsonAsync"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? IcmHandleOverride { get; set; }

    static Applied RunIcm(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildIcmArgs(route, op);

        try
        {
            string json;
            if (IcmHandleOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = IdeIcmChannel.HandleJsonAsync(args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadIcmOk(json);
            var pulse = TryReadIcmPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("icm_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "icm",
                Seat: seat,
                Go: "icm_desk",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "icm_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "icm",
                Go: "icm_desk",
                Path: route.Path,
                Reason: "icm_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "icm",
                Go: "icm_desk",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildIcmArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "command_id",
            route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "command_id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "command"));

        return args;
    }

    static bool TryReadIcmOk(string json)
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
            return root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("entries", out _)
                || root.TryGetProperty("bound", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadIcmPulse(string json, string op)
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
                return TruncPulse($"icm {op} fail {e}");

            if (root.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number)
                return TruncPulse($"icm {op} ok count={c.GetInt32()}");

            return TruncPulse($"icm {op} ok");
        }
        catch
        {
            return TruncPulse("icm " + op);
        }
    }
}
