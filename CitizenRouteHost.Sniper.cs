#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent scope|peek|target|aim|scope_clear — sync EditSniper.Dispatch.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake sniper JSON; live uses EditSniper.Dispatch.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? SniperCallOverride { get; set; }

    static Applied RunSniper(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "status" : route.Op!;
        var args = BuildSniperArgs(op, route);

        try
        {
            object result;
            if (SniperCallOverride is { } ov)
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

                result = EditSniper.Dispatch(store, session, args);
            }

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadUndoOk(json);
            var pulse = TryReadSniperPulse(json, op);
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

    static Dictionary<string, JsonElement> BuildSniperArgs(string op, CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        PutIfPresent(args, "from",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "select_from")
            ?? route.OldString);
        PutIfPresent(args, "till",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "till")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "to")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "select_till")
            ?? route.NewString);
        PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at"));
        PutIfPresent(args, "wire", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "wire"));
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
            if (root.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.String
                && phase.GetString() is { Length: > 0 } p)
                bits.Add(p);
            if (root.TryGetProperty("hold", out var hold) && hold.ValueKind == JsonValueKind.Object)
            {
                if (hold.TryGetProperty("phase", out var hp) && hp.ValueKind == JsonValueKind.String
                    && hp.GetString() is { Length: > 0 } hphase)
                    bits.Add("hold=" + hphase);
                if (hold.TryGetProperty("line_start", out var ls) && ls.TryGetInt32(out var a)
                    && hold.TryGetProperty("line_end", out var le) && le.TryGetInt32(out var b))
                    bits.Add("L" + a + "-" + b);
            }
            else if (root.TryGetProperty("line_start", out var ls2) && ls2.TryGetInt32(out var a2)
                && root.TryGetProperty("line_end", out var le2) && le2.TryGetInt32(out var b2))
                bits.Add("L" + a2 + "-" + b2);
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
