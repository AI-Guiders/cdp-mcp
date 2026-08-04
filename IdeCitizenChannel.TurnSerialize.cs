#nullable enable
using System.Text.Json;

namespace CdpMcp;
internal static partial class IdeCitizenChannel
{
    static string SerializeTurn(CitizenCompletions.TurnResult result, CitizenTurnMode mode, bool liveBound, bool execute, IReadOnlyList<CitizenRouteHost.Applied>? executed, IReadOnlyList<CitizenIntentRouter.Route>? routesOverride, CitizenPeerAck.Result? peerAck = null)
    {
        var hint = result.Hint;
        if (liveBound && result.Ok)
            hint = (hint is { Length: > 0 } ? hint + " · " : "") + "live desk bound (board/tm)";
        if (execute && executed is { Count: > 0 })
            hint = (hint is { Length: > 0 } ? hint + " · " : "") + "host executed " + executed.Count + " route(s)";
        if (peerAck is not null)
            hint = (hint is { Length: > 0 } ? hint + " · " : "") + "peer ack " + peerAck.Applied + "/" + (peerAck.Applied + peerAck.Dropped);
        var routes = routesOverride ?? result.Routes;
        return JsonSerializer.Serialize(new { schema = Schema, ok = result.Ok, op = "turn", mode = mode == CitizenTurnMode.Dialog ? "dialog" : "wire", dry_run = result.DryRun, execute, dialog = mode == CitizenTurnMode.Dialog ? CitizenDialogHistory.Pulse() : null, sticky = mode == CitizenTurnMode.Dialog ? CitizenStickyFacts.Pulse() : null, error = result.Error, hint, provider = result.Provider, model = result.Model, text = result.Text, injected = result.Built?.Injected, live_desk = liveBound, peer = peerAck?.Peer, peer_event = peerAck?.Event, afferent = result.Built?.AfferentPulse, message_count = result.Built?.Messages.Count, system_chars = result.Built?.System.Length, wire_intents = result.WireIntents?.Select(m => new { kind = m.Kind.ToString(), type = m.Type, intent = m.IntentText, fields = m.Fields }).ToArray(), routes = routes?.Select(r => new { verb = r.Verb.ToString(), raw = r.Raw, ok = r.Ok, go = r.Go, organ = r.Organ, path = r.Path, detail = r.Detail, scene = r.Scene, cmd = r.Cmd, reason = r.Reason }).ToArray(), executed = executed?.Select(a => new { verb = a.Verb, raw = a.Raw, ok = a.Ok, action = a.Action, seat = a.Seat, go = a.Go, path = a.Path, doc_id = a.DocId, cmd = a.Cmd, pulse = a.Pulse, reason = a.Reason }).ToArray() });
    }

    static bool Bool(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultTrue = false)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultTrue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => el.GetString()is "1" or "true" or "yes" or "on",
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            _ => defaultTrue
        };
    }

    static string? Arg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static int? IntArg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String
            && int.TryParse(el.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    static string Fail(string error, string hint) => JsonSerializer.Serialize(new { schema = Schema, ok = false, error, hint });
}