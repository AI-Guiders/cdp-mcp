#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static class CitizenBufferBuffer
{
    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var head = raw.Trim();
        string? op;
        if (head.StartsWith("buffer ", StringComparison.OrdinalIgnoreCase) || head.Equals("buffer", StringComparison.OrdinalIgnoreCase))
        {
            var rest = head.Length > 6 ? head[6..].TrimStart() : "";
            if (rest.Length == 0) op = "scene";
            else if (rest.StartsWith("read", StringComparison.OrdinalIgnoreCase)) op = "read";
            else if (rest.StartsWith("close", StringComparison.OrdinalIgnoreCase)) op = "close";
            else if (rest.StartsWith("scene", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("buffers", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("list", StringComparison.OrdinalIgnoreCase))
                op = "scene";
            else if (rest.StartsWith("open", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("doc_open", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("buffer_open", StringComparison.OrdinalIgnoreCase))
            {
                var openPath = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
                return string.IsNullOrWhiteSpace(openPath)
                    ? new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "open_path_empty")
                    : new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Open, raw, Ok: true, Path: openPath.Trim(), Go: "buffer");
            }
            else if (rest.StartsWith("diagnostics", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("diags", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("diag", StringComparison.OrdinalIgnoreCase))
                op = "diagnostics";
            else
                op = CitizenIntentRouter.ExtractKeyedValue(raw, "op") ?? "scene";
        }
        else if (head.StartsWith("doc_open", StringComparison.OrdinalIgnoreCase) || head.StartsWith("buffer_open", StringComparison.OrdinalIgnoreCase))
        {
            var openPath = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
            return string.IsNullOrWhiteSpace(openPath)
                ? new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "open_path_empty")
                : new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Open, raw, Ok: true, Path: openPath.Trim(), Go: "buffer");
        }
        else if (head.StartsWith("doc_read", StringComparison.OrdinalIgnoreCase) || head.StartsWith("buffer_read", StringComparison.OrdinalIgnoreCase)
            || head.Equals("read", StringComparison.OrdinalIgnoreCase) || head.StartsWith("read ", StringComparison.OrdinalIgnoreCase) || head.StartsWith("read path=", StringComparison.OrdinalIgnoreCase))
            op = "read";
        else if (head.StartsWith("doc_close", StringComparison.OrdinalIgnoreCase) || head.StartsWith("buffer_close", StringComparison.OrdinalIgnoreCase)
            || head.Equals("close", StringComparison.OrdinalIgnoreCase) || head.StartsWith("close ", StringComparison.OrdinalIgnoreCase) || head.StartsWith("close path=", StringComparison.OrdinalIgnoreCase))
            op = "close";
        else if (head.Equals("buffers", StringComparison.OrdinalIgnoreCase) || head.StartsWith("buffers ", StringComparison.OrdinalIgnoreCase)
            || head.Equals("doc_scene", StringComparison.OrdinalIgnoreCase) || head.StartsWith("doc_scene", StringComparison.OrdinalIgnoreCase)
            || head.Equals("buffer_scene", StringComparison.OrdinalIgnoreCase) || head.StartsWith("buffer_scene", StringComparison.OrdinalIgnoreCase))
            op = "scene";
        else if (head.StartsWith("doc_diagnostics", StringComparison.OrdinalIgnoreCase) || head.StartsWith("buffer_diagnostics", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buf_diagnostics", StringComparison.OrdinalIgnoreCase) || head.StartsWith("buf_diags", StringComparison.OrdinalIgnoreCase))
            op = "diagnostics";
        else
            op = CitizenIntentRouter.ExtractKeyedValue(raw, "op") ?? "scene";
        op = op.Trim().ToLowerInvariant() switch
        {
            "read" or "doc_read" or "buffer_read" => "read",
            "close" or "doc_close" or "buffer_close" => "close",
            "scene" or "buffers" or "list" or "doc_scene" or "buffer_scene" => "scene",
            "diagnostics" or "diags" or "diag" or "doc_diagnostics" or "buffer_diagnostics" or "buf_diagnostics" or "buf_diags" => "diagnostics",
            _ => op.Trim().ToLowerInvariant()
        };
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
