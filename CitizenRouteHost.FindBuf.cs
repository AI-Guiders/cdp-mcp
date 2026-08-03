#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent find_all|buf_find — sync DocumentEditPlane comfort find|find_all.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake comfort JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? FindBufCallOverride { get; set; }

    static Applied RunFindBuf(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "find" : route.Op!;
        if (string.IsNullOrEmpty(route.OldString))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: op,
                Path: route.Path,
                Reason: "findbuf_query_empty");
        }

        var args = BuildFindBufArgs(op, route);

        try
        {
            object result;
            if (FindBufCallOverride is { } ov)
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
            var ok = TryReadUndoOk(json);
            var pulse = TryReadFindBufPulse(json, op);
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

    static Dictionary<string, JsonElement> BuildFindBufArgs(string op, CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op),
            ["query"] = JsonSerializer.SerializeToElement(route.OldString!),
            ["scope"] = JsonSerializer.SerializeToElement(
                string.IsNullOrWhiteSpace(route.Detail) ? "buffer" : route.Detail!)
        };

        PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        PutIfPresent(args, "doc_id", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "doc_id"));

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
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add("n=" + n);
            if (root.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.String
                && sc.GetString() is { Length: > 0 } scope)
                bits.Add(scope);
            if (root.TryGetProperty("truncated", out var tr) && tr.ValueKind == JsonValueKind.True)
                bits.Add("trunc");
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
