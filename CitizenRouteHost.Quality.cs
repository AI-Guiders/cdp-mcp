#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent quality — QualityGates/AdxAssertions; place quality organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake quality board; live uses QualityGates / AdxAssertions.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? QualityHandleOverride { get; set; }

    static Applied RunQuality(CitizenIntentRouter.Route route)
    {
        var args = BuildQualityArgs(route);

        try
        {
            object board;
            if (QualityHandleOverride is { } ov)
            {
                board = ov(args);
            }
            else
            {
                var session = SessionResolver?.Invoke();
                var store = IdeLanguageTools.TryGetDocumentStore();
                var scope = OptArgString(args, "scope");
                var path = OptArgString(args, "path");
                var limit = OptArgInt(args, "limit") ?? 40;

                if (scope is "assert")
                {
                    board = AdxAssertions.Evaluate(session?.ProjectRoot);
                }
                else if (scope is "disk")
                {
                    board = QualityGates.EvaluateDisk(session?.ProjectRoot, limit);
                }
                else
                {
                    if (store is null)
                    {
                        return new Applied(
                            route.Raw,
                            route.Verb.ToString(),
                            Ok: false,
                            Action: "quality",
                            Go: "quality",
                            Reason: "no_doc_store");
                    }

                    board = string.IsNullOrWhiteSpace(path)
                        ? QualityGates.EvaluateStore(store, session?.ProjectRoot)
                        : QualityGates.EvaluatePath(store, session?.ProjectRoot, path!);
                }
            }

            var json = board is string s
                ? s
                : JsonSerializer.Serialize(board);
            var ok = TryReadQualityOk(json);
            var pulse = TryReadQualityPulse(json, OptArgString(args, "scope"));
            var seat = IdeDeskSeats.PlaceOrgan("quality");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "quality",
                Seat: seat,
                Go: "quality",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "quality_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "quality",
                Go: "quality",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildQualityArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var scope = route.Scene
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scope")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scan");
        if (!string.IsNullOrWhiteSpace(scope))
        {
            var normalized = scope.Trim().ToLowerInvariant() switch
            {
                "assertions" or "adx" => "assert",
                "project" or "map" => "disk",
                var s => s
            };
            args["scope"] = JsonSerializer.SerializeToElement(normalized);
        }

        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path"));

        var limitRaw = route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "limit");
        if (!string.IsNullOrWhiteSpace(limitRaw)
            && int.TryParse(limitRaw.Trim(), out var limit))
            args["limit"] = JsonSerializer.SerializeToElement(limit);

        return args;
    }

    static string? OptArgString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static int? OptArgInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String
            && int.TryParse(el.GetString(), out var parsed))
            return parsed;
        return null;
    }

    static bool TryReadQualityOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Gate severity (ok=false when fail×n) is board content — host still delivered.
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;

            return root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("schema", out _)
                || root.TryGetProperty("findings", out _)
                || root.TryGetProperty("scope", out _)
                || (root.TryGetProperty("ok", out var okEl)
                    && okEl.ValueKind != JsonValueKind.False);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadQualityPulse(string json, string? scope)
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
                return TruncPulse("quality · " + e);

            var tag = string.IsNullOrWhiteSpace(scope) ? "buffers" : scope;
            return TruncPulse("quality · " + tag);
        }
        catch
        {
            return TruncPulse("quality");
        }
    }
}
