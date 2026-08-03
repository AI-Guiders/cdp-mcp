#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent copy|cut|paste|clipboard — sync DocumentEditPlane comfort ops.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake comfort JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? ClipCallOverride { get; set; }

    static Applied RunClip(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "clipboard" : route.Op!;
        var args = BuildClipArgs(route);

        try
        {
            object result;
            if (ClipCallOverride is { } ov)
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
            var pulse = TryReadClipPulse(json, op);
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

    static Dictionary<string, JsonElement> BuildClipArgs(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "clipboard" : route.Op!;
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };

        // Re-parse wire keys so clip span/frame/place survive Route packing.
        PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from"));
        PutIfPresent(args, "text",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? route.NewString);
        PutIfPresent(args, "frame",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "frame")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "clip"));
        PutIfPresent(args, "place",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "place")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at_place"));
        PutIfPresent(args, "start_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "start_line"));
        PutIfPresent(args, "preserve", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "preserve"));
        if (string.Equals(CitizenIntentRouter.ExtractKeyedValue(route.Raw, "clear"), "true", StringComparison.OrdinalIgnoreCase))
            args["clear"] = JsonSerializer.SerializeToElement(true);

        return args;
    }

    static void PutIfPresent(Dictionary<string, JsonElement> args, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            args[key] = JsonSerializer.SerializeToElement(value);
    }

    static string? TryReadClipPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("frame", out var f) && f.ValueKind == JsonValueKind.String
                && f.GetString() is { Length: > 0 } frame)
                bits.Add(frame);
            if (root.TryGetProperty("chars", out var c) && c.TryGetInt32(out var chars))
                bits.Add("chars=" + chars);
            if (root.TryGetProperty("empty", out var e) && e.ValueKind == JsonValueKind.True)
                bits.Add("empty");
            else if (root.TryGetProperty("clipboard", out var clip) && clip.ValueKind == JsonValueKind.Object
                     && clip.TryGetProperty("count", out var cnt) && cnt.TryGetInt32(out var n))
                bits.Add("frames=" + n);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse(op);
        }
    }
}
