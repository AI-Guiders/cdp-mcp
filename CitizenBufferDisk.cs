#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Habitat;

namespace CdpMcp;

internal static class CitizenBufferDisk
{
    static readonly PrefixOpRule[] DiskPrefixRules =
    [
        new("disk_peek", "disk_peek", "diskpeek", "peek_disk"),
        new("keep_disk", "keep_disk", "keepdisk", "dont_reload", "don't_reload"),
        new("reload", "reload"),
    ];

    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var head = raw.Trim();
        var op = PrefixOpTable.Match(head, DiskPrefixRules)
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "op")
            ?? "reload";
        op = PrefixOpTable.Normalize(op, CitizenOpAliasMaps.Disk);
        if (op is not "reload" and not "keep_disk" and not "disk_peek")
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "disk_op_unknown");
        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        var pad = CitizenIntentRouter.ExtractKeyedValue(raw, "pad");
        return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Disk, raw, Ok: true, Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(pad) ? null : pad.Trim(), Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "reload" : route.Op!;
        var args = BuildDiskArgs(op, route);
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
            var pulse = TryReadDiskPulse(json, op);
            string? full = null; string? docId = null;
            CitizenEditResponse.TryReadEditMeta(json, out full, out docId);
            if (full is null) full = CitizenBufferComfort.TryReadRootPath(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 } && op is "reload" or "keep_disk")
                CitizenRouteHost.PublishGlassLandOpen(full);
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

    static Dictionary<string, JsonElement> BuildDiskArgs(string op, CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["op"] = JsonSerializer.SerializeToElement(op) };
        CitizenRouteHost.PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
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
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && err.GetString() is { Length: > 0 } error)
                bits.Add(error);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch { return CitizenRouteHost.TruncPulse(op); }
    }
}
