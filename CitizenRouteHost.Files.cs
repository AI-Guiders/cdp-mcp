#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent files — sync IdeFilesChannel.Handle; place files_desk organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake files JSON; live uses <see cref="IdeFilesChannel.Handle"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? FilesHandleOverride { get; set; }

    static Applied RunFiles(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildFilesArgs(route, op);

        try
        {
            string json;
            if (FilesHandleOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "files",
                        Go: "files_desk",
                        Path: route.Path,
                        Reason: "doc_store_unbound");
                }

                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "files",
                        Go: "files_desk",
                        Path: route.Path,
                        Reason: "no_session");
                }

                var result = IdeFilesChannel.Handle(store, session, args);
                json = result is string s
                    ? s
                    : JsonSerializer.Serialize(result);
            }

            var ok = TryReadFilesOk(json);
            var pulse = TryReadFilesPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("files_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "files",
                Seat: seat,
                Go: "files_desk",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "files_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "files",
                Go: "files_desk",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildFilesArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "path",
            route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "to")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "name")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));

        PutIfPresent(args, "where",
            CitizenIntentRouter.ExtractKeyedValue(raw, "where"));

        PutIfPresent(args, "query",
            CitizenIntentRouter.ExtractKeyedValue(raw, "query")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "q"));

        var depthRaw = CitizenIntentRouter.ExtractKeyedValue(raw, "depth");
        if (depthRaw is { Length: > 0 } && int.TryParse(depthRaw, out var depth))
            args["depth"] = JsonSerializer.SerializeToElement(depth);

        return args;
    }

    static bool TryReadFilesOk(string json)
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
                || root.TryGetProperty("cwd", out _)
                || root.TryGetProperty("schema", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadFilesPulse(string json, string op)
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
                return TruncPulse($"files {op} fail {e}");

            if (root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number)
                return TruncPulse($"files {op} ok total={t.GetInt32()}");

            return TruncPulse($"files {op} ok");
        }
        catch
        {
            return TruncPulse("files " + op);
        }
    }
}
