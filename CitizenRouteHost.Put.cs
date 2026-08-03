#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent put — sync DocumentEditPlane comfort put.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake comfort JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? PutCallOverride { get; set; }

    static Applied RunPut(CitizenIntentRouter.Route route)
    {
        const string op = "put";
        var args = BuildPutArgs(route);

        try
        {
            object result;
            if (PutCallOverride is { } ov)
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
            var pulse = TryReadPutPulse(json);
            string? full = null;
            string? docId = null;
            TryReadEditMeta(json, out full, out docId);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 })
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

    static Dictionary<string, JsonElement> BuildPutArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("put"),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };

        PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        PutIfPresent(args, "text",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "content")
            ?? route.NewString);
        PutIfPresent(args, "frame",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "frame")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "clip"));
        PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at"));
        PutIfPresent(args, "place",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "place")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at_place"));
        PutIfPresent(args, "preserve", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "preserve"));
        PutIfPresent(args, "overwrite", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "overwrite"));

        var sniper = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "sniper")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "dest");
        if (string.Equals(sniper, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sniper, "sniper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.Scene, "sniper", StringComparison.OrdinalIgnoreCase))
            args["sniper"] = JsonSerializer.SerializeToElement(true);

        return args;
    }

    static string? TryReadPutPulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "put" };
            if (root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String
                && m.GetString() is { Length: > 0 } mode)
                bits.Add(mode);
            if (root.TryGetProperty("chars", out var c) && c.TryGetInt32(out var n))
                bits.Add("chars=" + n);
            else if (root.TryGetProperty("chars", out var cs) && cs.ValueKind == JsonValueKind.Number)
                bits.Add("chars=" + cs.GetRawText());
            if (root.TryGetProperty("frame", out var f) && f.ValueKind == JsonValueKind.String
                && f.GetString() is { Length: > 0 } frame)
                bits.Add(frame);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("put");
        }
    }
}
