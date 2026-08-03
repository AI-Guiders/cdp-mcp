#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent reload|keep_disk|disk_peek — sync DocumentEditPlane disk hygiene.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake disk JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? DiskCallOverride { get; set; }

    static Applied RunDisk(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "reload" : route.Op!;
        var args = BuildDiskArgs(op, route);

        try
        {
            object result;
            if (DiskCallOverride is { } ov)
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
            var pulse = TryReadDiskPulse(json, op);
            string? full = null;
            string? docId = null;
            TryReadEditMeta(json, out full, out docId);
            if (full is null)
                full = TryReadRootPath(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 } && op is "reload" or "keep_disk")
                PublishGlassLandOpen(full);
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

    static Dictionary<string, JsonElement> BuildDiskArgs(string op, CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        var padRaw = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "pad") ?? route.Detail;
        if (!string.IsNullOrWhiteSpace(padRaw) && int.TryParse(padRaw.Trim(), out var pad))
            args["pad"] = JsonSerializer.SerializeToElement(pad);

        return args;
    }

    static string? TryReadDiskPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add("n=" + n);
            else if (root.TryGetProperty("count", out var cs) && cs.ValueKind == JsonValueKind.Number)
                bits.Add("n=" + cs.GetRawText());
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
