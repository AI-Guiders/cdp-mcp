#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent peel — async IdePeelChannel; place peel organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake peel JSON; live uses <see cref="IdePeelChannel.HandleAsync"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, ICdpBackendModule>, IReadOnlyDictionary<string, JsonElement>, string>? PeelHandleOverride { get; set; }

    static Applied RunPeel(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "place" : route.Op!;
        if (op is "place")
        {
            var seat = IdeDeskSeats.PlaceOrgan("peel");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: true,
                Action: "peel",
                Seat: seat,
                Go: "peel",
                Pulse: "peel · place");
        }

        var session = SessionResolver?.Invoke();
        if (session is null && PeelHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "peel",
                Go: "peel",
                Path: route.Path,
                Reason: "no_session");
        }

        var args = BuildPeelArgs(route, op);

        try
        {
            string json;
            if (PeelHandleOverride is { } ov)
            {
                json = ov(session ?? new SessionContext(), new Dictionary<string, ICdpBackendModule>(), args);
            }
            else
            {
                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>();
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = IdePeelChannel.HandleAsync(session!, byDomain, args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadPeelOk(json);
            var pulse = TryReadPeelPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("peel");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "peel",
                Seat: seat,
                Go: "peel",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "peel_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "peel",
                Go: "peel",
                Path: route.Path,
                Reason: "peel_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "peel",
                Go: "peel",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildPeelArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var raw = route.Raw;

        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file_path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));
        PutIfPresent(args, "members", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "members")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "member_names")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "member"));
        PutIfPresent(args, "out", route.NewString
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "out")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "output")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "output_file_path"));

        var apply = op is "apply";
        args["apply"] = JsonSerializer.SerializeToElement(apply);

        if (int.TryParse(CitizenIntentRouter.ExtractKeyedValue(raw, "line"), out var line))
            args["line"] = JsonSerializer.SerializeToElement(line);
        if (int.TryParse(CitizenIntentRouter.ExtractKeyedValue(raw, "column"), out var col))
            args["column"] = JsonSerializer.SerializeToElement(col);

        var dep = CitizenIntentRouter.ExtractKeyedValue(raw, "add_dependent_upon");
        if (dep is not null)
        {
            var on = dep.Equals("true", StringComparison.OrdinalIgnoreCase)
                || dep.Equals("1", StringComparison.OrdinalIgnoreCase);
            args["add_dependent_upon"] = JsonSerializer.SerializeToElement(on);
        }

        return args;
    }

    static bool TryReadPeelOk(string json)
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
            return root.TryGetProperty("pulse", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadPeelPulse(string json, string op)
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
                return TruncPulse("peel " + op + " · " + e);

            return TruncPulse("peel · " + op);
        }
        catch
        {
            return null;
        }
    }
}