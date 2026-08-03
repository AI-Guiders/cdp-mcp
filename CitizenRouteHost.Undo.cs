#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent undo|redo — sync DocumentEditPlane comfort ops.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake comfort JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? UndoCallOverride { get; set; }

    static Applied RunUndo(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "undo" : route.Op!;
        var args = BuildUndoArgs(op, route.Path);

        try
        {
            object result;
            if (UndoCallOverride is { } ov)
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
            var pulse = TryReadUndoPulse(json, op);
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

    static Dictionary<string, JsonElement> BuildUndoArgs(string op, string? path)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };
        if (!string.IsNullOrWhiteSpace(path))
            args["path"] = JsonSerializer.SerializeToElement(path);
        return args;
    }

    static bool TryReadUndoOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            return false;
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadUndoError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                return TruncPulse(err.GetString());
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string? TryReadUndoPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("undone", out var u) && u.ValueKind == JsonValueKind.String
                && u.GetString() is { Length: > 0 } undone)
                bits.Add(undone);
            if (root.TryGetProperty("redone", out var r) && r.ValueKind == JsonValueKind.String
                && r.GetString() is { Length: > 0 } redone)
                bits.Add(redone);
            if (root.TryGetProperty("undo_left", out var ul) && ul.TryGetInt32(out var undoLeft))
                bits.Add("undo=" + undoLeft);
            if (root.TryGetProperty("redo_left", out var rl) && rl.TryGetInt32(out var redoLeft))
                bits.Add("redo=" + redoLeft);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse(op);
        }
    }
}
