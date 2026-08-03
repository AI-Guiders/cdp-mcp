#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent scratch — sync DocumentEditPlane comfort scratch.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake comfort JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? ScratchCallOverride { get; set; }

    static Applied RunScratch(CitizenIntentRouter.Route route)
    {
        const string op = "scratch";
        var args = BuildScratchArgs(route);

        try
        {
            object result;
            if (ScratchCallOverride is { } ov)
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
            var pulse = TryReadScratchPulse(json);
            string? full = null;
            string? docId = null;
            TryReadEditMeta(json, out full, out docId);
            if (full is null)
                full = TryReadRootPath(json) ?? TryReadScratchAnchor(json);
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

    static Dictionary<string, JsonElement> BuildScratchArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("scratch"),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };

        PutIfPresent(args, "ext",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "ext") ?? route.Detail);
        PutIfPresent(args, "text",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "content")
            ?? route.NewString);

        return args;
    }

    static string? TryReadScratchAnchor(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("anchor", out var a) && a.ValueKind == JsonValueKind.String)
                return a.GetString();
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string? TryReadScratchPulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "scratch" };
            if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("path", out var mp) && mp.ValueKind == JsonValueKind.String
                && mp.GetString() is { Length: > 0 } metaPath)
                bits.Add(ShortNavLeaf(metaPath));
            else if (root.TryGetProperty("anchor", out var a) && a.ValueKind == JsonValueKind.String
                     && a.GetString() is { Length: > 0 } anchor)
                bits.Add(ShortNavLeaf(anchor));
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("scratch");
        }
    }
}
