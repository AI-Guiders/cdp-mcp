#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent edit|anchor — route + buffer edit_op=anchor (OOA&D peel).</summary>
internal static class CitizenBufferEdit
{
    internal static CitizenIntentRouter.Route Route(string raw)
    {
    
            var editOp = CitizenIntentRouter.ExtractKeyedValue(raw, "edit_op") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "op");
            if (!string.IsNullOrWhiteSpace(editOp))
            {
                var normalized = editOp.Trim().ToLowerInvariant();
                if (normalized is "set_text" or "set-text" or "settext")
                {
                    return new CitizenIntentRouter.Route(
                        CitizenIntentRouter.Verb.Refuse,
                        raw,
                        Ok: true,
                        Reason: "edit_refuse_set_text — use edit_op=anchor (or replace/create/append)");
                }
    
                if (normalized is not "anchor")
                    return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Unknown, raw, Ok: false, Reason: "edit_op_unsupported_" + normalized);
            }
    
            var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
            var anchor = CitizenIntentRouter.ExtractKeyedValue(raw, "anchor")
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at")
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "wire");
            var text = CitizenIntentRouter.ExtractKeyedValue(raw, "text")
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "body")
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "new")
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "new_string");
            var oldString = CitizenIntentRouter.ExtractKeyedValue(raw, "old_string")
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "old");
            var place = CitizenIntentRouter.ExtractKeyedValue(raw, "place") ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at_place");
    
            if (string.IsNullOrWhiteSpace(path))
                return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Edit, raw, Ok: false, Reason: "edit_path_required", Go: "buffer");
            if (string.IsNullOrWhiteSpace(anchor))
                return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Edit, raw, Ok: false, Path: path, Reason: "edit_anchor_required", Go: "buffer");
            if (text is null)
                return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Edit, raw, Ok: false, Path: path, Detail: anchor, Reason: "edit_text_required", Go: "buffer");
    
            place = string.IsNullOrWhiteSpace(place) ? "replace" : place.Trim().ToLowerInvariant();
            place = place switch
            {
                "pre" or "before" => "before",
                "post" or "after" => "after",
                "replace" or "overwrite" or "swap" => "replace",
                _ => place
            };
            if (place is not "before" and not "after" and not "replace")
                return new CitizenIntentRouter.Route(CitizenIntentRouter.Verb.Edit, raw, Ok: false, Path: path, Detail: anchor, Reason: "edit_place_invalid", Go: "buffer");
    
            return new CitizenIntentRouter.Route(
                CitizenIntentRouter.Verb.Edit,
                raw,
                Ok: true,
                Path: path.Trim(),
                Detail: anchor.Trim(),
                OldString: string.IsNullOrWhiteSpace(oldString) ? null : oldString,
                NewString: text,
                Op: place,
                Go: "buffer");
        
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
    
            var path = route.Path;
            var anchor = route.Detail;
            if (string.IsNullOrWhiteSpace(path))
            {
                return new CitizenRouteHost.Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "edit",
                    Reason: "edit_path_empty");
            }
    
            if (string.IsNullOrWhiteSpace(anchor))
            {
                return new CitizenRouteHost.Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "edit",
                    Path: path,
                    Reason: "edit_anchor_empty");
            }
    
            if (route.NewString is null)
            {
                return new CitizenRouteHost.Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "edit",
                    Path: path,
                    Reason: "edit_text_empty");
            }
    
            var place = string.IsNullOrWhiteSpace(route.Op) ? "replace" : route.Op!;
            var args = BuildEditArgs(route, path, anchor, route.NewString, place);
    
            try
            {
                object result;
                if (callOverride is { } ov)
                {
                    result = ov(args);
                }
                else
                {
                    var store = IdeLanguageTools.TryGetDocumentStore();
                    if (store is null)
                    {
                        return new CitizenRouteHost.Applied(
                            route.Raw,
                            route.Verb.ToString(),
                            Ok: false,
                            Action: "edit",
                            Path: path,
                            Reason: "doc_store_unbound");
                    }
    
                    var session = CitizenRouteHost.SessionResolver?.Invoke();
                    if (session is null)
                    {
                        return new CitizenRouteHost.Applied(
                            route.Raw,
                            route.Verb.ToString(),
                            Ok: false,
                            Action: "edit",
                            Path: path,
                            Reason: "no_session");
                    }
    
                    var byDomain = CitizenRouteHost.ByDomainResolver?.Invoke()
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
                var ok = CitizenEditResponse.TryReadEditOk(jsonText);
                var pulse = CitizenEditResponse.TryReadEditPulse(jsonText, place);
                string? full = null;
                string? docId = null;
                CitizenEditResponse.TryReadEditMeta(jsonText, out full, out docId);
                var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
                if (full is { Length: > 0 })
                    CitizenRouteHost.PublishGlassLandOpen(full);
                return new CitizenRouteHost.Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: ok,
                    Action: "edit",
                    Seat: seat,
                    Go: "editor_scene",
                    Path: full ?? path,
                    DocId: docId,
                    Pulse: pulse,
                    Reason: ok ? null : (CitizenRouteHost.TryReadLifecycleError(jsonText) ?? pulse ?? "edit_failed"));
            }
            catch (Exception ex)
            {
                return new CitizenRouteHost.Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "edit",
                    Path: path,
                    Reason: ex.GetType().Name + ": " + ex.Message);
            }
        
    }

    static Dictionary<string, JsonElement> BuildEditArgs(
            CitizenIntentRouter.Route route,
            string path,
            string anchor,
            string text,
            string place)
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
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
    
            CitizenRouteHost.PutIfPresent(
                args,
                "old_string",
                CitizenIntentRouter.ExtractKeyedValue(route.Raw, "old_string")
                ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "old")
                ?? route.OldString);
            CitizenRouteHost.PutBoolIfPresent(args, "force", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "force"));
    
            return args;
        }}