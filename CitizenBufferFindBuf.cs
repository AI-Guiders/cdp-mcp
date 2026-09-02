#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Habitat;

namespace CdpMcp;

internal static class CitizenBufferFindBuf
{
    static readonly PrefixOpRule[] FindBufPrefixRules =
    [
        new("find_all", "find_all", "findall", "buf_find_all", "buffer_find_all"),
        new("find", "buf_find", "buffer_find", "find_in", "find_buffer", "find ", "search ", "find", "search"),
    ];

    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var head = raw.Trim();
        var op = PrefixOpTable.Match(head, FindBufPrefixRules)
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "op")
            ?? "find";
        op = PrefixOpTable.Normalize(op, CitizenOpAliasMaps.FindBuf);
        if (op is not "find" and not "find_all")
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "findbuf_op_unknown");
        var query = CitizenIntentRouter.ExtractKeyedValue(raw, "query")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "q")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "pattern");
        if (string.IsNullOrEmpty(query)
            && (head.StartsWith("find_all ", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("buf_find ", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("buffer_find ", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("find_in ", StringComparison.OrdinalIgnoreCase)))
            query = ExtractPositionalFindBufQuery(head);
        if (string.IsNullOrEmpty(query))
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.FindBuf, raw, Ok: false, Op: op, Go: "buffer", Reason: "findbuf_query_required");
        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file");
        var scope = CitizenIntentRouter.ExtractKeyedValue(raw, "scope") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "in") ?? "buffer";
        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.FindBuf, raw, Ok: true, Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            OldString: query,
            Detail: string.IsNullOrWhiteSpace(scope) ? "buffer" : scope.Trim(), Go: "buffer");
    }

    internal static bool LooksLikeBufferFindScope(string raw)
    {
        var scope = CitizenIntentRouter.ExtractKeyedValue(raw, "scope") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "in");
        if (string.IsNullOrWhiteSpace(scope)) return false;
        scope = scope.Trim();
        return scope.Equals("buffer", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("file", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("doc", StringComparison.OrdinalIgnoreCase);
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "find" : route.Op!;
        if (string.IsNullOrEmpty(route.OldString))
            return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "findbuf_query_empty");
        var args = BuildFindBufArgs(op, route);
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
            var pulse = TryReadFindBufPulse(json, op);
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

    static string? ExtractPositionalFindBufQuery(string head)
    {
        var sp = head.IndexOf(' ');
        if (sp < 0) return null;
        var rest = head[(sp + 1)..].Trim();
        if (rest.Length == 0) return null;
        if (rest.Contains('=', StringComparison.Ordinal))
        {
            var tokSp = rest.IndexOf(' ');
            var first = tokSp < 0 ? rest : rest[..tokSp];
            if (first.Contains('=', StringComparison.Ordinal)) return null;
            return first.Trim().Trim('"');
        }
        return rest.Trim().Trim('"');
    }

    static Dictionary<string, JsonElement> BuildFindBufArgs(string op, CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op),
            ["query"] = JsonSerializer.SerializeToElement(route.OldString!),
            ["scope"] = JsonSerializer.SerializeToElement(string.IsNullOrWhiteSpace(route.Detail) ? "buffer" : route.Detail!)
        };
        CitizenRouteHost.PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        CitizenRouteHost.PutIfPresent(args, "doc_id", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "doc_id"));
        if (string.Equals(CitizenIntentRouter.ExtractKeyedValue(route.Raw, "regex"), "true", StringComparison.OrdinalIgnoreCase))
            args["regex"] = JsonSerializer.SerializeToElement(true);
        var ignore = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "ignore_case");
        if (string.Equals(ignore, "true", StringComparison.OrdinalIgnoreCase))
            args["ignore_case"] = JsonSerializer.SerializeToElement(true);
        else if (string.Equals(ignore, "false", StringComparison.OrdinalIgnoreCase))
            args["ignore_case"] = JsonSerializer.SerializeToElement(false);
        return args;
    }

    static string? TryReadFindBufPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n)) bits.Add("n=" + n);
            if (root.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.String && sc.GetString() is { Length: > 0 } scope) bits.Add(scope);
            if (root.TryGetProperty("truncated", out var tr) && tr.ValueKind == JsonValueKind.True) bits.Add("trunc");
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && err.GetString() is { Length: > 0 } error) bits.Add(error);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch { return CitizenRouteHost.TruncPulse(op); }
    }
}
