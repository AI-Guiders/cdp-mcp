#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent edit — sync buffer edit_op=anchor via DocumentEditPlane.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake edit JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? EditCallOverride { get; set; }

    static Applied RunEdit(CitizenIntentRouter.Route route)
    {
        var path = route.Path;
        var anchor = route.Detail;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "edit",
                Reason: "edit_path_empty");
        }

        if (string.IsNullOrWhiteSpace(anchor))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "edit",
                Path: path,
                Reason: "edit_anchor_empty");
        }

        if (route.NewString is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "edit",
                Path: path,
                Reason: "edit_text_empty");
        }

        var place = string.IsNullOrWhiteSpace(route.Op) ? "replace" : route.Op!;
        var args = BuildEditArgs(path, anchor, route.NewString, place);

        try
        {
            object result;
            if (EditCallOverride is { } ov)
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
                        Action: "edit",
                        Path: path,
                        Reason: "doc_store_unbound");
                }

                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "edit",
                        Path: path,
                        Reason: "no_session");
                }

                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var json = DocumentEditPlane.DispatchAsync(
                        "cdp_buffer",
                        store,
                        session,
                        byDomain,
                        args,
                        cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                result = json;
            }

            var jsonText = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadEditOk(jsonText);
            var pulse = TryReadEditPulse(jsonText, place);
            string? full = null;
            string? docId = null;
            TryReadEditMeta(jsonText, out full, out docId);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 })
                PublishGlassLandOpen(full);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "edit",
                Seat: seat,
                Go: "editor_scene",
                Path: full ?? path,
                DocId: docId,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(jsonText) ?? pulse ?? "edit_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "edit",
                Path: path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildEditArgs(string path, string anchor, string text, string place)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("edit"),
            ["edit_op"] = JsonSerializer.SerializeToElement("anchor"),
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["anchor"] = JsonSerializer.SerializeToElement(anchor),
            ["text"] = JsonSerializer.SerializeToElement(text),
            ["place"] = JsonSerializer.SerializeToElement(place),
            ["flush"] = JsonSerializer.SerializeToElement(true),
            ["diagnose"] = JsonSerializer.SerializeToElement(true)
        };
    }

    static bool TryReadEditOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            return root.TryGetProperty("op", out _) || root.TryGetProperty("meta", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadEditPulse(string json, string place)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("op", out var opEl) && opEl.ValueKind == JsonValueKind.String
                && opEl.GetString() is { Length: > 0 } op)
                return TruncPulse("edit " + op + " place=" + place);

            return TruncPulse("edit anchor place=" + place);
        }
        catch
        {
            return TruncPulse("edit anchor place=" + place);
        }
    }

    static void TryReadEditMeta(string json, out string? path, out string? docId)
    {
        path = null;
        docId = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
                return;
            if (meta.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                path = p.GetString();
            if (meta.TryGetProperty("doc_id", out var d) && d.ValueKind == JsonValueKind.String)
                docId = d.GetString();
        }
        catch
        {
            /* best-effort */
        }
    }
}
