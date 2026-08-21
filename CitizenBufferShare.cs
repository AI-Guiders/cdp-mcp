#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static class CitizenBufferShare
{
    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        var with = CitizenIntentRouter.ExtractKeyedValue(raw, "with") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "to");
        var from = CitizenIntentRouter.ExtractKeyedValue(raw, "from");
        var body = CitizenIntentRouter.ExtractKeyedValue(raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "content")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "notes");
        var ask = CitizenIntentRouter.ExtractKeyedValue(raw, "ask");
        var detail = !string.IsNullOrWhiteSpace(from) ? from.Trim()
            : !string.IsNullOrWhiteSpace(with) ? with.Trim() : ask;
        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.Share, raw, Ok: true, Op: "share",
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
            NewString: body,
            Scene: string.IsNullOrWhiteSpace(ask) ? null : ask.Trim(),
            Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        const string op = "share";
        var args = BuildShareArgs(route);
        try
        {
            object result;
            if (callOverride is { } ov) result = ov(args);
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                    return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "doc_store_unbound");
                var session = CitizenRouteHost.SessionResolver?.Invoke();
                if (session is null)
                    return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "no_session");
                var byDomain = CitizenRouteHost.ByDomainResolver?.Invoke() ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                result = DocumentEditPlane.DispatchAsync("cdp_buffer", store, session, byDomain, args, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = CitizenBufferComfort.TryReadUndoOk(json);
            var pulse = TryReadSharePulse(json);
            string? full = null; string? docId = null;
            CitizenEditResponse.TryReadEditMeta(json, out full, out docId);
            if (full is null) full = CitizenBufferComfort.TryReadRootPath(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
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

    static Dictionary<string, JsonElement> BuildShareArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("share"),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };
        CitizenRouteHost.PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        CitizenRouteHost.PutIfPresent(args, "with", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "with") ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "to"));
        CitizenRouteHost.PutIfPresent(args, "from", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from"));
        CitizenRouteHost.PutIfPresent(args, "body",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "content")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "notes")
            ?? route.NewString);
        CitizenRouteHost.PutIfPresent(args, "ask", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "ask") ?? route.Scene);
        CitizenRouteHost.PutIfPresent(args, "dir", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "dir") ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "inbox"));
        CitizenRouteHost.PutIfPresent(args, "anchor", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor") ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at"));
        CitizenRouteHost.PutIfPresent(args, "start_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "start_line"));
        CitizenRouteHost.PutIfPresent(args, "end_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "end_line"));
        CitizenRouteHost.PutIfPresent(args, "depth", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "depth"));
        return args;
    }

    static string? TryReadSharePulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "share" };
            if (root.TryGetProperty("with", out var w) && w.ValueKind == JsonValueKind.String && w.GetString() is { Length: > 0 } with)
                bits.Add(with);
            if (root.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String && f.GetString() is { Length: > 0 } from)
                bits.Add("from=" + from);
            if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String && st.GetString() is { Length: > 0 } status)
                bits.Add(status);
            if (root.TryGetProperty("chars", out var c) && c.TryGetInt32(out var n))
                bits.Add("chars=" + n);
            else if (root.TryGetProperty("chars", out var cs) && cs.ValueKind == JsonValueKind.Number)
                bits.Add("chars=" + cs.GetRawText());
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && err.GetString() is { Length: > 0 } error)
                bits.Add(error);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch { return CitizenRouteHost.TruncPulse("share"); }
    }
}
