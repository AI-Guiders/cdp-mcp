#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent back|forward|nav — sync DocumentEditPlane comfort nav ops.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake comfort JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? NavCallOverride { get; set; }

    static Applied RunNav(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "nav" : route.Op!;
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };

        try
        {
            object result;
            if (NavCallOverride is { } ov)
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
            var pulse = TryReadNavPulse(json, op);
            string? full = null;
            string? docId = null;
            TryReadEditMeta(json, out full, out docId);
            // NavStep may put locus at root (wire), not meta.path
            if (full is null)
                full = TryReadRootPath(json) ?? TryReadLocus(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 } && !full.StartsWith('['))
                PublishGlassLandOpen(full);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: op,
                Seat: seat,
                Go: "editor_scene",
                Path: full,
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
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string? TryReadRootPath(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string? TryReadLocus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("locus", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string? TryReadNavPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("locus", out var loc) && loc.ValueKind == JsonValueKind.String
                && loc.GetString() is { Length: > 0 } locus)
                bits.Add(ShortNavLeaf(locus));
            else if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
                     && p.GetString() is { Length: > 0 } path)
                bits.Add(ShortNavLeaf(path));

            JsonElement navEl = default;
            var hasNav = root.TryGetProperty("nav", out navEl) && navEl.ValueKind == JsonValueKind.Object;
            if (hasNav && navEl.TryGetProperty("back", out var nb) && nb.TryGetInt32(out var navBack))
                bits.Add("back=" + navBack);
            else if (root.TryGetProperty("back", out var b) && b.TryGetInt32(out var back))
                bits.Add("back=" + back);
            else if (root.TryGetProperty("nav_back", out var nback) && nback.TryGetInt32(out var nb2))
                bits.Add("back=" + nb2);

            if (hasNav && navEl.TryGetProperty("forward", out var nf) && nf.TryGetInt32(out var navFwd))
                bits.Add("fwd=" + navFwd);
            else if (root.TryGetProperty("forward", out var f) && f.TryGetInt32(out var fwd))
                bits.Add("fwd=" + fwd);
            else if (root.TryGetProperty("nav_forward", out var nfwd) && nfwd.TryGetInt32(out var nf2))
                bits.Add("fwd=" + nf2);

            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add("n=" + n);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse(op);
        }
    }

    static string ShortNavLeaf(string path)
    {
        var leaf = path;
        var slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        if (slash >= 0 && slash < path.Length - 1)
            leaf = path[(slash + 1)..];
        return leaf.Trim('[', ']');
    }
}
