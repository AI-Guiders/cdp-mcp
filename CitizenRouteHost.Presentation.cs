#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent cide_presentation — sync IdeCidePresentationChannel; place organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake presentation JSON; live uses <see cref="IdeCidePresentationChannel.HandleJson"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? PresentationHandleOverride { get; set; }

    static Applied RunPresentation(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildPresentationArgs(route, op);

        try
        {
            var json = PresentationHandleOverride is { } ov
                ? ov(args)
                : IdeCidePresentationChannel.HandleJson(args);
            var ok = TryReadPresentationOk(json);
            var pulse = TryReadPresentationPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("cide_presentation");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "presentation",
                Seat: seat,
                Go: "cide_presentation",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "presentation_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "presentation",
                Go: "cide_presentation",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildPresentationArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        PutIfPresent(args, "topology",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "topology")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "value")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "presentation"));
        PutIfPresent(args, "tier", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "tier"));
        PutIfPresent(args, "pfd_primary", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "pfd_primary"));
        PutIfPresent(args, "mfd_primary", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "mfd_primary"));
        PutIfPresent(args, "pfd_status_strip",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "pfd_status_strip"));
        PutIfPresent(args, "forward_status_strip",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "forward_status_strip"));
        PutIfPresent(args, "mfd_page",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "mfd_page")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "page"));
        PutIfPresent(args, "instruments",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "instruments"));

        return args;
    }

    static bool TryReadPresentationOk(string json)
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
            return root.TryGetProperty("op", out _) || root.TryGetProperty("topology", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadPresentationPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("presentation " + op + " " + pulse);

            var bits = new List<string> { "presentation", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("topology", out var topo) && topo.ValueKind == JsonValueKind.String
                && topo.GetString() is { Length: > 0 } t)
                bits.Add(t);
            if (root.TryGetProperty("tier", out var tier) && tier.ValueKind == JsonValueKind.String
                && tier.GetString() is { Length: > 0 } tr)
                bits.Add("tier=" + tr);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("presentation " + op);
        }
    }
}
