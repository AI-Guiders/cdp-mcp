#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent analysis — sync MetaDispatch cdp_analysis_scene; place analysis_scene organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake analysis JSON; live uses MetaDispatchResolver("cdp_analysis_scene", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? AnalysisDispatchOverride { get; set; }

    static Applied RunAnalysis(CitizenIntentRouter.Route route)
    {
        var feature = string.IsNullOrWhiteSpace(route.Op) ? "map" : route.Op!;
        var args = BuildAnalysisArgs(route, feature);

        try
        {
            string json;
            if (AnalysisDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_analysis_scene", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadAnalysisOk(json);
            var pulse = TryReadAnalysisPulse(json, feature);
            var seat = IdeDeskSeats.PlaceOrgan("analysis_scene");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "analysis",
                Seat: seat,
                Go: "analysis_scene",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "analysis_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "analysis",
                Go: "analysis_scene",
                Path: route.Path,
                Reason: "analysis_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "analysis",
                Go: "analysis_scene",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildAnalysisArgs(CitizenIntentRouter.Route route, string feature)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["feature"] = JsonSerializer.SerializeToElement(feature)
        };

        var raw = route.Raw;
        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file_path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));
        PutIfPresent(args, "anchor", route.NewString
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "from"));
        PutIfPresent(args, "mode", CitizenIntentRouter.ExtractKeyedValue(raw, "mode"));
        PutIfPresent(args, "scope", CitizenIntentRouter.ExtractKeyedValue(raw, "scope")
            ?? (feature is "clones" && route.Tool is { Length: > 0 } ? route.Tool : null));
        PutIfPresent(args, "preset", CitizenIntentRouter.ExtractKeyedValue(raw, "preset"));
        PutIfPresent(args, "search_in", CitizenIntentRouter.ExtractKeyedValue(raw, "search_in"));

        return args;
    }

    static bool TryReadAnalysisOk(string json)
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
                || root.TryGetProperty("scene", out _)
                || root.TryGetProperty("features", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadAnalysisPulse(string json, string feature)
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
                return TruncPulse("analysis " + feature + " · " + e);

            return TruncPulse("analysis · " + feature);
        }
        catch
        {
            return null;
        }
    }
}
