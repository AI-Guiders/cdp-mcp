#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent webcam — sync IdeWebcamChannel; place webcam_desk organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake webcam JSON; live uses <see cref="IdeWebcamChannel.HandleJson"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? WebcamHandleOverride { get; set; }

    static Applied RunWebcam(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && WebcamHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "webcam",
                Go: "webcam_desk",
                Reason: "no_session");
        }

        var args = BuildWebcamArgs(route, op);

        try
        {
            object result;
            if (WebcamHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeWebcamChannel.HandleJson(session!, args);

            var json = result is string s
                ? s
                : JsonSerializer.Serialize(result);
            var ok = TryReadWebcamOk(json);
            var pulse = TryReadWebcamPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("webcam_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "webcam",
                Seat: seat,
                Go: "webcam_desk",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "webcam_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "webcam",
                Go: "webcam_desk",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildWebcamArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        foreach (var key in WebcamStringKeys)
        {
            var val = CitizenIntentRouter.ExtractKeyedValue(raw, key);
            if (val is { Length: > 0 })
                PutIfPresent(args, key, val);
        }

        // Path-like leftovers from router Path= when keyed path/hwnd/process/title set.
        if (route.Path is { Length: > 0 }
            && !args.ContainsKey("path")
            && !args.ContainsKey("hwnd")
            && !args.ContainsKey("process")
            && !args.ContainsKey("title"))
        {
            if (op is "window")
                PutIfPresent(args, "title", route.Path);
            else
                PutIfPresent(args, "path", route.Path);
        }

        return args;
    }

    static readonly string[] WebcamStringKeys =
    [
        "path", "device", "index", "count", "ms", "seconds", "region",
        "hwnd", "process", "title", "maximize", "enlarge",
        "lang", "model", "out", "dir", "query"
    ];

    static bool TryReadWebcamOk(string json)
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
                || root.TryGetProperty("op", out _)
                || root.TryGetProperty("schema", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadWebcamPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("webcam " + op + " " + pulse);

            return TruncPulse("webcam " + op);
        }
        catch
        {
            return TruncPulse("webcam " + op);
        }
    }
}
