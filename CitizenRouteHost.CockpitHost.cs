#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent cockpit_host — sync IdeCockpitHostChannel; place organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake cockpit_host JSON; live uses <see cref="IdeCockpitHostChannel.HandleJson"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? CockpitHostHandleOverride { get; set; }

    static Applied RunCockpitHost(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildCockpitHostArgs(route, op);

        try
        {
            var json = CockpitHostHandleOverride is { } ov
                ? ov(args)
                : IdeCockpitHostChannel.HandleJson(args);
            var ok = TryReadCockpitHostOk(json);
            var pulse = TryReadCockpitHostPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("cockpit_host");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "cockpit_host",
                Seat: seat,
                Go: "cockpit_host",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "cockpit_host_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "cockpit_host",
                Go: "cockpit_host",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildCockpitHostArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        PutIfPresent(args, "path",
            route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "exe"));

        return args;
    }

    static bool TryReadCockpitHostOk(string json)
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
            return root.TryGetProperty("op", out _) || root.TryGetProperty("gui_host", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadCockpitHostPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return pulse;

            var gui = root.TryGetProperty("gui_host", out var g) && g.ValueKind == JsonValueKind.String
                ? g.GetString()
                : null;
            var pid = root.TryGetProperty("pid", out var pidEl) && pidEl.ValueKind == JsonValueKind.Number
                ? pidEl.GetInt32().ToString()
                : null;
            return gui is { Length: > 0 }
                ? $"cockpit_host {op} ok gui={gui}" + (pid is null ? "" : $" pid={pid}")
                : $"cockpit_host {op} ok";
        }
        catch
        {
            return $"cockpit_host {op}";
        }
    }
}
