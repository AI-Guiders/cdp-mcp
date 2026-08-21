#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent replace_all — route + buffer execute (OOA&D peel).</summary>
internal static class CitizenBufferReplaceAll
{
    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        var query = CitizenIntentRouter.ExtractKeyedValue(raw, "query")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "old")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "old_string")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "pattern");
        var text = CitizenIntentRouter.ExtractKeyedValue(raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "new")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "new_string")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "body")
            ?? "";
        if (string.IsNullOrWhiteSpace(path))
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.ReplaceAll, raw, Ok: false, Reason: "replace_all_path_required", Go: "buffer");
        if (string.IsNullOrEmpty(query))
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.ReplaceAll, raw, Ok: false, Path: path.Trim(), Reason: "replace_all_query_required", Go: "buffer");
        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.ReplaceAll, raw, Ok: true, Op: "replace_all",
            Path: path.Trim(), OldString: query, NewString: text, Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        const string op = "replace_all";
        if (string.IsNullOrWhiteSpace(route.Path))
            return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Reason: "replace_all_path_empty");
        if (string.IsNullOrEmpty(route.OldString))
            return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "replace_all_query_empty");
        var args = BuildReplaceAllArgs(route);
        try
        {
            object result;
            if (callOverride is { } ov)
                result = ov(args);
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                    return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "doc_store_unbound");
                var session = CitizenRouteHost.SessionResolver?.Invoke();
                if (session is null)
                    return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "no_session");
                var byDomain = CitizenRouteHost.ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                result = DocumentEditPlane.DispatchAsync("cdp_buffer", store, session, byDomain, args, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = CitizenBufferComfort.TryReadUndoOk(json);
            var pulse = TryReadReplaceAllPulse(json);
            string? full = null;
            string? docId = null;
            CitizenEditResponse.TryReadEditMeta(json, out full, out docId);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 })
                CitizenRouteHost.PublishGlassLandOpen(full);
            return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: ok, Action: op, Seat: seat, Go: "editor_scene",
                Path: full ?? route.Path, DocId: docId, Pulse: pulse,
                Reason: ok ? null : (CitizenRouteHost.TryReadLifecycleError(json) ?? CitizenBufferComfort.TryReadUndoError(json) ?? pulse ?? op + "_failed"));
        }
        catch (Exception ex)
        {
            return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildReplaceAllArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("replace_all"),
            ["flush"] = JsonSerializer.SerializeToElement(true),
            ["path"] = JsonSerializer.SerializeToElement(route.Path!),
            ["query"] = JsonSerializer.SerializeToElement(route.OldString!),
            ["text"] = JsonSerializer.SerializeToElement(route.NewString ?? "")
        };
        if (string.Equals(CitizenIntentRouter.ExtractKeyedValue(route.Raw, "regex"), "true", StringComparison.OrdinalIgnoreCase))
            args["regex"] = JsonSerializer.SerializeToElement(true);
        if (string.Equals(CitizenIntentRouter.ExtractKeyedValue(route.Raw, "ignore_case"), "true", StringComparison.OrdinalIgnoreCase))
            args["ignore_case"] = JsonSerializer.SerializeToElement(true);
        return args;
    }

    static string? TryReadReplaceAllPulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "replace_all" };
            if (root.TryGetProperty("replaced", out var r) && r.TryGetInt32(out var n))
                bits.Add("n=" + n);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch { return CitizenRouteHost.TruncPulse("replace_all"); }
    }
}
