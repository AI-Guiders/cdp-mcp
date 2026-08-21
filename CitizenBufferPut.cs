#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent put — route + buffer execute (OOA&D peel).</summary>
internal static class CitizenBufferPut
{
    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        var text = CitizenIntentRouter.ExtractKeyedValue(raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "content");
        var frame = CitizenIntentRouter.ExtractKeyedValue(raw, "frame")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "clip");
        var anchor = CitizenIntentRouter.ExtractKeyedValue(raw, "anchor") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at");
        var place = CitizenIntentRouter.ExtractKeyedValue(raw, "place") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at_place");
        var sniper = CitizenIntentRouter.ExtractKeyedValue(raw, "sniper")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "dest");
        var useSniper = string.Equals(sniper, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sniper, "sniper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(place, "sniper", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(anchor) && !useSniper)
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Put, raw, Ok: false, Op: "put", Reason: "path_or_dest_required", Go: "buffer");
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(frame))
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Put, raw, Ok: false, Op: "put", Path: path, Reason: "body_or_frame_required", Go: "buffer");
        var scene = useSniper ? "sniper" : place;
        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.Put, raw, Ok: true, Op: "put",
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(anchor) ? frame : anchor.Trim(),
            NewString: text, Scene: scene, Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        const string op = "put";
        var args = BuildPutArgs(route);
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
            var pulse = TryReadPutPulse(json);
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

    static Dictionary<string, JsonElement> BuildPutArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("put"),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };
        CitizenRouteHost.PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        CitizenRouteHost.PutIfPresent(args, "text",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "content")
            ?? route.NewString);
        CitizenRouteHost.PutIfPresent(args, "frame",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "frame")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "clip"));
        CitizenRouteHost.PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at"));
        CitizenRouteHost.PutIfPresent(args, "place",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "place")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at_place"));
        CitizenRouteHost.PutIfPresent(args, "preserve", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "preserve"));
        CitizenRouteHost.PutIfPresent(args, "overwrite", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "overwrite"));
        var sniper = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "sniper")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "dest");
        if (string.Equals(sniper, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sniper, "sniper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.Scene, "sniper", StringComparison.OrdinalIgnoreCase))
            args["sniper"] = JsonSerializer.SerializeToElement(true);
        return args;
    }

    static string? TryReadPutPulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "put" };
            if (root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } mode)
                bits.Add(mode);
            if (root.TryGetProperty("chars", out var c) && c.TryGetInt32(out var n))
                bits.Add("chars=" + n);
            else if (root.TryGetProperty("chars", out var cs) && cs.ValueKind == JsonValueKind.Number)
                bits.Add("chars=" + cs.GetRawText());
            if (root.TryGetProperty("frame", out var f) && f.ValueKind == JsonValueKind.String && f.GetString() is { Length: > 0 } frame)
                bits.Add(frame);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch { return CitizenRouteHost.TruncPulse("put"); }
    }
}
