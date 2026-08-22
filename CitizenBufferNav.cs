#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Habitat;

namespace CdpMcp;

internal static class CitizenBufferNav
{
    static readonly PrefixOpRule[] NavPrefixRules =
    [
        new("forward", "forward"),
        new("back", "back"),
        new("recent_files", "recent_files", "recent"),
    ];

    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var head = raw.Trim();
        string? op;
        if (head.StartsWith("nav_status", StringComparison.OrdinalIgnoreCase)
            || head.Equals("nav", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("nav ", StringComparison.OrdinalIgnoreCase))
            op = CitizenIntentRouter.ExtractKeyedValue(raw, "op") is { Length: > 0 } keyed ? keyed : "nav";
        else
            op = PrefixOpTable.Match(head, NavPrefixRules)
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "op")
                ?? "nav";
        op = PrefixOpTable.Normalize(op, CitizenOpAliasMaps.Nav);
        if (op is not "back" and not "forward" and not "nav" and not "recent_files")
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "nav_op_unknown");
        return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Nav, raw, Ok: true, Op: op, Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(CitizenIntentRouter.Route route, Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "nav" : route.Op!;
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["op"] = JsonSerializer.SerializeToElement(op), ["flush"] = JsonSerializer.SerializeToElement(true) };
        try
        {
            object result;
            if (callOverride is { } ov) result = ov(args);
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null) return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Reason: "doc_store_unbound");
                var session = CitizenRouteHost.SessionResolver?.Invoke();
                if (session is null) return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Reason: "no_session");
                var byDomain = CitizenRouteHost.ByDomainResolver?.Invoke() ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                result = DocumentEditPlane.DispatchAsync("cdp_buffer", store, session, byDomain, args, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = CitizenBufferComfort.TryReadUndoOk(json);
            var pulse = TryReadNavPulse(json, op);
            string? full = null; string? docId = null;
            CitizenEditResponse.TryReadEditMeta(json, out full, out docId);
            if (full is null) full = CitizenBufferComfort.TryReadRootPath(json) ?? CitizenBufferComfort.TryReadLocus(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 } && !full.StartsWith('[')) CitizenRouteHost.PublishGlassLandOpen(full);
            return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: ok, Action: op, Seat: seat, Go: "editor_scene", Path: full, DocId: docId, Pulse: pulse,
                Reason: ok ? null : (CitizenRouteHost.TryReadLifecycleError(json) ?? CitizenBufferComfort.TryReadUndoError(json) ?? pulse ?? op + "_failed"));
        }
        catch (Exception ex) { return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Reason: ex.GetType().Name + ": " + ex.Message); }
    }

    static string? TryReadNavPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("locus", out var loc) && loc.ValueKind == JsonValueKind.String && loc.GetString() is { Length: > 0 } locus) bits.Add(CitizenBufferComfort.ShortNavLeaf(locus));
            else if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } path) bits.Add(CitizenBufferComfort.ShortNavLeaf(path));
            JsonElement navEl = default; var hasNav = root.TryGetProperty("nav", out navEl) && navEl.ValueKind == JsonValueKind.Object;
            if (hasNav && navEl.TryGetProperty("back", out var nb) && nb.TryGetInt32(out var navBack)) bits.Add("back=" + navBack);
            else if (root.TryGetProperty("back", out var b) && b.TryGetInt32(out var back)) bits.Add("back=" + back);
            else if (root.TryGetProperty("nav_back", out var nback) && nback.TryGetInt32(out var nb2)) bits.Add("back=" + nb2);
            if (hasNav && navEl.TryGetProperty("forward", out var nf) && nf.TryGetInt32(out var navFwd)) bits.Add("fwd=" + navFwd);
            else if (root.TryGetProperty("forward", out var f) && f.TryGetInt32(out var fwd)) bits.Add("fwd=" + fwd);
            else if (root.TryGetProperty("nav_forward", out var nfwd) && nfwd.TryGetInt32(out var nf2)) bits.Add("fwd=" + nf2);
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n)) bits.Add("n=" + n);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch { return CitizenRouteHost.TruncPulse(op); }
    }
}
