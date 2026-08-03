#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent read|close|buffers|doc_diagnostics — sync DocumentEditPlane core.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake buffer JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? BufferCallOverride { get; set; }

    static Applied RunBuffer(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildBufferArgs(op, route);

        try
        {
            object result;
            if (BufferCallOverride is { } ov)
            {
                result = ov(args);
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
                        Action: op,
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
                        Action: op,
                        Path: route.Path,
                        Reason: "no_session");
                }

                var byDomain = ByDomainResolver?.Invoke()
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
            var ok = TryReadBufferOk(json, op);
            var pulse = TryReadBufferPulse(json, op);
            string? full = null;
            string? docId = null;
            TryReadEditMeta(json, out full, out docId);
            if (full is null)
                full = TryReadRootPath(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: op,
                Seat: seat,
                Go: "editor_scene",
                Path: full ?? route.Path,
                DocId: docId,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? TryReadUndoError(json) ?? pulse ?? op + "_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: op,
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildBufferArgs(string op, CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        PutIfPresent(args, "doc_id",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "doc_id")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "doc")
            ?? route.OldString);

        if (op == "read")
        {
            PutIntIfPresent(args, "start_line",
                CitizenIntentRouter.ExtractKeyedValue(route.Raw, "start_line")
                ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from_line")
                ?? route.Detail);
            PutIntIfPresent(args, "end_line",
                CitizenIntentRouter.ExtractKeyedValue(route.Raw, "end_line")
                ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "to_line")
                ?? route.NewString);
        }

        if (op == "close")
        {
            PutBoolIfPresent(args, "flush", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "flush"));
            PutBoolIfPresent(args, "discard", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "discard"));
        }

        if (op == "diagnostics")
        {
            PutIfPresent(args, "scope", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scope"));
            PutBoolIfPresent(args, "flush", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "flush"));
            PutBoolIfPresent(args, "force", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "force"));
            PutBoolIfPresent(args, "refresh", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "refresh"));
        }

        return args;
    }

    static void PutIntIfPresent(Dictionary<string, JsonElement> args, string key, string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out var n))
            args[key] = JsonSerializer.SerializeToElement(n);
    }

    static void PutBoolIfPresent(Dictionary<string, JsonElement> args, string key, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;
        if (!TryParseLooseBool(raw.Trim(), out var b))
            return;
        args[key] = JsonSerializer.SerializeToElement(b);
    }

    static bool TryParseLooseBool(string raw, out bool value)
    {
        if (bool.TryParse(raw, out value))
            return true;
        if (raw.Equals("1", StringComparison.Ordinal) || raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (raw.Equals("0", StringComparison.Ordinal) || raw.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    static bool TryReadBufferOk(string json, string op)
    {
        if (TryReadUndoOk(json))
            return true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            if (root.TryGetProperty("schema", out var sch) && sch.ValueKind == JsonValueKind.String
                && sch.GetString() is { Length: > 0 } schema)
            {
                if (op == "read" && schema.StartsWith("doc_read", StringComparison.Ordinal))
                    return true;
                if (op == "scene" && schema.StartsWith("doc_scene", StringComparison.Ordinal))
                    return true;
            }
        }
        catch
        {
            /* best-effort */
        }

        return false;
    }

    static string? TryReadBufferPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add("n=" + n);
            if (root.TryGetProperty("flushed", out var fl) && fl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                bits.Add(fl.GetBoolean() ? "flushed" : "noflush");
            if (root.TryGetProperty("discarded_dirty", out var dd) && dd.ValueKind == JsonValueKind.True)
                bits.Add("discarded");
            if (root.TryGetProperty("cached", out var cached) && cached.ValueKind == JsonValueKind.True)
                bits.Add("cached");
            if (root.TryGetProperty("start_line", out var sl) && sl.TryGetInt32(out var a)
                && root.TryGetProperty("end_line", out var el) && el.TryGetInt32(out var b))
                bits.Add("L" + a + "-" + b);
            else if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("line_count", out var lc) && lc.TryGetInt32(out var lines))
                bits.Add("lines=" + lines);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } error)
                bits.Add(error);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse(op);
        }
    }
}
