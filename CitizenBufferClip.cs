#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Habitat;

namespace CdpMcp;

/// <summary>Citizen @intent copy|cut|paste|clipboard — route + buffer execute (OOA&D peel).</summary>
internal static class CitizenBufferClip
{
    static readonly PrefixOpRule[] ClipPrefixRules =
    [
        new("cut", "cut"),
        new("paste", "paste"),
        new("clipboard_clear", "clipboard_clear", "clip_clear"),
        new("clipboard", "clipboard", "clip ", "clip"),
        new("copy", "copy"),
    ];

    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var head = raw.Trim();
        var op = PrefixOpTable.Match(head, ClipPrefixRules)
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "op")
            ?? "clipboard";
        op = PrefixOpTable.Normalize(op, CitizenOpAliasMaps.Clip);

        if (op is not "copy" and not "cut" and not "paste" and not "clipboard" and not "clipboard_clear")
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "clip_op_unknown");

        if (op == "clipboard"
            && string.Equals(CitizenIntentRouter.ExtractKeyedValue(raw, "clear"), "true", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(CitizenIntentRouter.ExtractKeyedValue(raw, "frame"))
            && string.IsNullOrWhiteSpace(CitizenIntentRouter.ExtractKeyedValue(raw, "id")))
            op = "clipboard_clear";

        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        var anchor = CitizenIntentRouter.ExtractKeyedValue(raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "from");
        var text = CitizenIntentRouter.ExtractKeyedValue(raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "body");
        var frame = CitizenIntentRouter.ExtractKeyedValue(raw, "frame")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "clip");
        var place = CitizenIntentRouter.ExtractKeyedValue(raw, "place") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at_place");

        if ((op is "copy" or "cut")
            && string.IsNullOrWhiteSpace(path)
            && string.IsNullOrWhiteSpace(anchor)
            && string.IsNullOrWhiteSpace(text))
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Clip, raw, Ok: false, Op: op, Reason: "clip_span_required", Go: "buffer");

        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.Clip,
            raw,
            Ok: true,
            Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(anchor) ? frame : anchor.Trim(),
            NewString: text,
            Scene: string.IsNullOrWhiteSpace(place) ? frame : place.Trim(),
            Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "clipboard" : route.Op!;
        var args = BuildClipArgs(route);

        try
        {
            object result;
            if (callOverride is { } ov)
                result = ov(args);
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                {
                    return new CitizenRouteHost.Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: op,
                        Path: route.Path,
                        Reason: "doc_store_unbound");
                }

                var session = CitizenRouteHost.SessionResolver?.Invoke();
                if (session is null)
                {
                    return new CitizenRouteHost.Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: op,
                        Path: route.Path,
                        Reason: "no_session");
                }

                var byDomain = CitizenRouteHost.ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                result = DocumentEditPlane.DispatchAsync(
                        "cdp_buffer",
                        store,
                        session,
                        byDomain,
                        args,
                        cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = CitizenBufferComfort.TryReadUndoOk(json);
            var pulse = TryReadClipPulse(json, op);
            string? full = null;
            string? docId = null;
            CitizenEditResponse.TryReadEditMeta(json, out full, out docId);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 })
                CitizenRouteHost.PublishGlassLandOpen(full);
            return new CitizenRouteHost.Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: op,
                Seat: seat,
                Go: "editor_scene",
                Path: full ?? route.Path,
                DocId: docId,
                Pulse: pulse,
                Reason: ok ? null : (CitizenRouteHost.TryReadLifecycleError(json) ?? CitizenBufferComfort.TryReadUndoError(json) ?? pulse ?? op + "_failed"));
        }
        catch (Exception ex)
        {
            return new CitizenRouteHost.Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: op,
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildClipArgs(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "clipboard" : route.Op!;
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };

        CitizenRouteHost.PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        CitizenRouteHost.PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from"));
        CitizenRouteHost.PutIfPresent(args, "text",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? route.NewString);
        CitizenRouteHost.PutIfPresent(args, "frame",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "frame")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "clip"));
        CitizenRouteHost.PutIfPresent(args, "place",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "place")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at_place"));
        CitizenRouteHost.PutIfPresent(args, "start_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "start_line"));
        CitizenRouteHost.PutIfPresent(args, "preserve", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "preserve"));
        if (string.Equals(CitizenIntentRouter.ExtractKeyedValue(route.Raw, "clear"), "true", StringComparison.OrdinalIgnoreCase))
            args["clear"] = JsonSerializer.SerializeToElement(true);

        return args;
    }

    static string? TryReadClipPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("frame", out var f) && f.ValueKind == JsonValueKind.String
                && f.GetString() is { Length: > 0 } frame)
                bits.Add(frame);
            if (root.TryGetProperty("chars", out var c) && c.TryGetInt32(out var chars))
                bits.Add("chars=" + chars);
            if (root.TryGetProperty("empty", out var e) && e.ValueKind == JsonValueKind.True)
                bits.Add("empty");
            else if (root.TryGetProperty("clipboard", out var clip) && clip.ValueKind == JsonValueKind.Object
                     && clip.TryGetProperty("count", out var cnt) && cnt.TryGetInt32(out var n))
                bits.Add("frames=" + n);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return CitizenRouteHost.TruncPulse(op);
        }
    }
}
