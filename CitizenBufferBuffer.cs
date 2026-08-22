#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Habitat;

namespace CdpMcp;

internal static class CitizenBufferBuffer
{
    static readonly PrefixOpRule[] BufferOpenRules =
    [
        new("open", "doc_open", "buffer_open", "open"),
    ];

    static readonly PrefixOpRule[] BufferSubRules =
    [
        new("read", "read"),
        new("close", "close"),
        new("scene", "scene", "buffers", "list"),
        new("diagnostics", "diagnostics", "diags", "diag"),
    ];

    static readonly PrefixOpRule[] BufferTopRules =
    [
        new("read", "doc_read", "buffer_read", "read ", "read path=", "read"),
        new("close", "doc_close", "buffer_close", "close ", "close path=", "close"),
        new("scene", "buffers", "buffers ", "doc_scene", "buffer_scene"),
        new("diagnostics", "doc_diagnostics", "buffer_diagnostics", "buf_diagnostics", "buf_diags"),
    ];

    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var head = raw.Trim();
        if (PrefixOpTable.Match(head, BufferOpenRules) is not null
            || PrefixOpTable.MatchSubcommand(head, "buffer", BufferOpenRules) is not null)
            return RouteOpen(raw);

        string? op;
        if (head.StartsWith("buffer ", StringComparison.OrdinalIgnoreCase) || head.Equals("buffer", StringComparison.OrdinalIgnoreCase))
        {
            op = PrefixOpTable.MatchSubcommand(head, "buffer", BufferSubRules, whenEmpty: "scene")
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "op")
                ?? "scene";
        }
        else
            op = PrefixOpTable.Match(head, BufferTopRules)
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "op")
                ?? "scene";
        op = PrefixOpTable.Normalize(op, CitizenOpAliasMaps.Buffer);
        if (op is not "read" and not "close" and not "scene" and not "diagnostics")
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "buffer_op_unknown");
        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        var docId = CitizenIntentRouter.ExtractKeyedValue(raw, "doc_id") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "doc");
        var start = CitizenIntentRouter.ExtractKeyedValue(raw, "start_line") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "from_line");
        var end = CitizenIntentRouter.ExtractKeyedValue(raw, "end_line") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "to_line");
        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.Buffer, raw, Ok: true, Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            OldString: string.IsNullOrWhiteSpace(docId) ? null : docId.Trim(),
            Detail: string.IsNullOrWhiteSpace(start) ? null : start.Trim(),
            NewString: string.IsNullOrWhiteSpace(end) ? null : end.Trim(), Go: "buffer");
    }

    static CitizenIntentRouter.Route RouteOpen(string raw)
    {
        var openPath = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        return string.IsNullOrWhiteSpace(openPath)
            ? new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "open_path_empty")
            : new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Open, raw, Ok: true, Path: openPath.Trim(), Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildBufferArgs(op, route);
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
            var ok = TryReadBufferOk(json, op);
            var pulse = TryReadBufferPulse(json, op);
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

    static Dictionary<string, JsonElement> BuildBufferArgs(string op, CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["op"] = JsonSerializer.SerializeToElement(op) };
        CitizenRouteHost.PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        CitizenRouteHost.PutIfPresent(args, "doc_id",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "doc_id")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "doc")
            ?? route.OldString);
        if (op == "read")
        {
            CitizenRouteHost.PutIntIfPresent(args, "start_line",
                CitizenIntentRouter.ExtractKeyedValue(route.Raw, "start_line")
                ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from_line")
                ?? route.Detail);
            CitizenRouteHost.PutIntIfPresent(args, "end_line",
                CitizenIntentRouter.ExtractKeyedValue(route.Raw, "end_line")
                ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "to_line")
                ?? route.NewString);
        }
        if (op == "close")
        {
            CitizenRouteHost.PutBoolIfPresent(args, "flush", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "flush"));
            CitizenRouteHost.PutBoolIfPresent(args, "discard", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "discard"));
        }
        if (op == "diagnostics")
        {
            CitizenRouteHost.PutIfPresent(args, "scope", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scope"));
            CitizenRouteHost.PutBoolIfPresent(args, "flush", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "flush"));
            CitizenRouteHost.PutBoolIfPresent(args, "force", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "force"));
            CitizenRouteHost.PutBoolIfPresent(args, "refresh", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "refresh"));
        }
        return args;
    }

    static bool TryReadBufferOk(string json, string op)
    {
        if (CitizenBufferComfort.TryReadUndoOk(json)) return true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && err.GetString() is { Length: > 0 })
                return false;
            if (root.TryGetProperty("schema", out var sch) && sch.ValueKind == JsonValueKind.String && sch.GetString() is { Length: > 0 } schema)
            {
                if (op == "read" && schema.StartsWith("doc_read", StringComparison.Ordinal)) return true;
                if (op == "scene" && schema.StartsWith("doc_scene", StringComparison.Ordinal)) return true;
            }
        }
        catch { /* best-effort */ }
        return false;
    }

    static string? TryReadBufferPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n)) bits.Add("n=" + n);
            if (root.TryGetProperty("flushed", out var fl) && fl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                bits.Add(fl.GetBoolean() ? "flushed" : "noflush");
            if (root.TryGetProperty("discarded_dirty", out var dd) && dd.ValueKind == JsonValueKind.True) bits.Add("discarded");
            if (root.TryGetProperty("cached", out var cached) && cached.ValueKind == JsonValueKind.True) bits.Add("cached");
            if (root.TryGetProperty("start_line", out var sl) && sl.TryGetInt32(out var a)
                && root.TryGetProperty("end_line", out var el) && el.TryGetInt32(out var b))
                bits.Add("L" + a + "-" + b);
            else if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("line_count", out var lc) && lc.TryGetInt32(out var lines))
                bits.Add("lines=" + lines);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && err.GetString() is { Length: > 0 } error)
                bits.Add(error);
            if (op == "read" && root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String && textEl.GetString() is { Length: > 0 } body)
            {
                var one = body.Replace('\r', ' ').Replace('\n', ' ');
                while (one.Contains("  ", StringComparison.Ordinal))
                    one = one.Replace("  ", " ", StringComparison.Ordinal);
                one = one.Trim();
                if (one.Length > 0) bits.Add("· " + one);
            }
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits), op == "read" ? CitizenRouteHost.InventoryObservePulseMax : 240);
        }
        catch { return CitizenRouteHost.TruncPulse(op); }
    }
}
