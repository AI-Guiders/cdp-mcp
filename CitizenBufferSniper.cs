#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static class CitizenBufferSniper
{
    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var head = raw.Trim();
        string? op;
        if (head.StartsWith("sniper", StringComparison.OrdinalIgnoreCase))
        {
            var rest = head.Length > 6 ? head[6..].TrimStart() : "";
            if (rest.Length == 0 || rest.StartsWith("status", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("show", StringComparison.OrdinalIgnoreCase))
                op = "status";
            else if (rest.StartsWith("clear", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("scope_clear", StringComparison.OrdinalIgnoreCase))
                op = "clear";
            else if (rest.StartsWith("scope", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("set", StringComparison.OrdinalIgnoreCase))
                op = "scope";
            else if (rest.StartsWith("peek", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("view", StringComparison.OrdinalIgnoreCase))
                op = "peek";
            else if (rest.StartsWith("target", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("outline", StringComparison.OrdinalIgnoreCase))
                op = "target";
            else if (rest.StartsWith("aim", StringComparison.OrdinalIgnoreCase))
                op = "aim";
            else if (CitizenIntentRouter.ExtractKeyedValue(raw, "from") is { Length: > 0 }
                || CitizenIntentRouter.ExtractKeyedValue(raw, "anchor") is { Length: > 0 })
                op = "scope";
            else
                op = CitizenIntentRouter.ExtractKeyedValue(raw, "op") ?? "status";
        }
        else if (head.StartsWith("scope_clear", StringComparison.OrdinalIgnoreCase) || head.StartsWith("sniperclear", StringComparison.OrdinalIgnoreCase) || head.StartsWith("sniper_clear", StringComparison.OrdinalIgnoreCase))
            op = "clear";
        else if (head.StartsWith("scope", StringComparison.OrdinalIgnoreCase) || head.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            op = "scope";
        else if (head.Equals("peek", StringComparison.OrdinalIgnoreCase) || head.StartsWith("peek ", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("peek wire=", StringComparison.OrdinalIgnoreCase) || head.StartsWith("peek pad=", StringComparison.OrdinalIgnoreCase))
            op = "peek";
        else if (head.StartsWith("target", StringComparison.OrdinalIgnoreCase) || head.StartsWith("outline", StringComparison.OrdinalIgnoreCase))
            op = "target";
        else if (head.StartsWith("aim", StringComparison.OrdinalIgnoreCase))
            op = "aim";
        else
            op = CitizenIntentRouter.ExtractKeyedValue(raw, "op") ?? "status";
        op = op.Trim().ToLowerInvariant() switch
        {
            "scope" or "set" or "lock" => "scope",
            "peek" or "view" => "peek",
            "target" or "outline" => "target",
            "aim" => "aim",
            "clear" or "scope_clear" or "sniperclear" or "sniper_clear" => "clear",
            "status" or "show" => "status",
            _ => op.Trim().ToLowerInvariant()
        };
        if (op is not "scope" and not "peek" and not "target" and not "aim" and not "clear" and not "status")
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "sniper_op_unknown");
        var from = CitizenIntentRouter.ExtractKeyedValue(raw, "from")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "wire")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at");
        var till = CitizenIntentRouter.ExtractKeyedValue(raw, "till") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "to");
        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        var pad = CitizenIntentRouter.ExtractKeyedValue(raw, "pad");
        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.Sniper, raw, Ok: true, Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            OldString: string.IsNullOrWhiteSpace(from) ? null : from.Trim(),
            NewString: string.IsNullOrWhiteSpace(till) ? null : till.Trim(),
            Detail: string.IsNullOrWhiteSpace(pad) ? null : pad.Trim(), Go: "scope");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "status" : route.Op!;
        var args = BuildSniperArgs(op, route);
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
                result = EditSniper.Dispatch(store, session, args);
            }
            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = CitizenBufferComfort.TryReadUndoOk(json);
            var pulse = TryReadSniperPulse(json, op);
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

    static Dictionary<string, JsonElement> BuildSniperArgs(string op, CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["op"] = JsonSerializer.SerializeToElement(op) };
        CitizenRouteHost.PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        CitizenRouteHost.PutIfPresent(args, "from",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "select_from")
            ?? route.OldString);
        CitizenRouteHost.PutIfPresent(args, "till",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "till")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "to")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "select_till")
            ?? route.NewString);
        CitizenRouteHost.PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at"));
        CitizenRouteHost.PutIfPresent(args, "wire", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "wire"));
        var padRaw = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "pad") ?? route.Detail;
        if (!string.IsNullOrWhiteSpace(padRaw) && int.TryParse(padRaw.Trim(), out var pad))
            args["pad"] = JsonSerializer.SerializeToElement(pad);
        var maxRaw = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "max");
        if (!string.IsNullOrWhiteSpace(maxRaw) && int.TryParse(maxRaw.Trim(), out var max))
            args["max"] = JsonSerializer.SerializeToElement(max);
        return args;
    }

    static string? TryReadSniperPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.String && phase.GetString() is { Length: > 0 } p)
                bits.Add(p);
            if (root.TryGetProperty("hold", out var hold) && hold.ValueKind == JsonValueKind.Object)
            {
                if (hold.TryGetProperty("phase", out var hp) && hp.ValueKind == JsonValueKind.String && hp.GetString() is { Length: > 0 } hphase)
                    bits.Add("hold=" + hphase);
                if (hold.TryGetProperty("line_start", out var ls) && ls.TryGetInt32(out var a)
                    && hold.TryGetProperty("line_end", out var le) && le.TryGetInt32(out var b))
                    bits.Add("L" + a + "-" + b);
            }
            else if (root.TryGetProperty("line_start", out var ls2) && ls2.TryGetInt32(out var a2)
                && root.TryGetProperty("line_end", out var le2) && le2.TryGetInt32(out var b2))
                bits.Add("L" + a2 + "-" + b2);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && err.GetString() is { Length: > 0 } error)
                bits.Add(error);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch { return CitizenRouteHost.TruncPulse(op); }
    }
}
