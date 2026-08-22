#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Habitat;

namespace CdpMcp;

/// <summary>Citizen @intent undo|redo|edit_history — route + buffer execute (OOA&D peel).</summary>
internal static class CitizenBufferUndo
{
    static readonly PrefixOpRule[] UndoPrefixRules =
    [
        new("redo", "redo"),
        new("history", "edit_history"),
    ];

    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var head = raw.Trim();
        var op = PrefixOpTable.Match(head, UndoPrefixRules)
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "op")
            ?? "undo";
        op = PrefixOpTable.Normalize(op, CitizenOpAliasMaps.Undo);

        if (op is not "undo" and not "redo" and not "history")
            return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "undo_op_unknown");

        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.Undo,
            raw,
            Ok: true,
            Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "undo" : route.Op!;
        var args = BuildUndoArgs(op, route.Path);

        try
        {
            object result;
            if (callOverride is { } ov)
                result = ov(args);
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                {
                    return new CitizenRouteHost.Applied(
                        route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "doc_store_unbound");
                }

                var session = CitizenRouteHost.SessionResolver?.Invoke();
                if (session is null)
                {
                    return new CitizenRouteHost.Applied(
                        route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "no_session");
                }

                var byDomain = CitizenRouteHost.ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                result = DocumentEditPlane.DispatchAsync("cdp_buffer", store, session, byDomain, args, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = CitizenBufferComfort.TryReadUndoOk(json);
            var pulse = CitizenBufferComfort.TryReadUndoPulse(json, op);
            string? full = null;
            string? docId = null;
            CitizenEditResponse.TryReadEditMeta(json, out full, out docId);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            if (full is { Length: > 0 })
                CitizenRouteHost.PublishGlassLandOpen(full);
            return new CitizenRouteHost.Applied(
                route.Raw, route.Verb.ToString(), Ok: ok, Action: op, Seat: seat, Go: "editor_scene",
                Path: full ?? route.Path, DocId: docId, Pulse: pulse,
                Reason: ok ? null : (CitizenRouteHost.TryReadLifecycleError(json) ?? CitizenBufferComfort.TryReadUndoError(json) ?? pulse ?? op + "_failed"));
        }
        catch (Exception ex)
        {
            return new CitizenRouteHost.Applied(
                route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path,
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
}
