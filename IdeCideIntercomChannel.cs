#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Desk Intercom voice — dual-cockpit @PF/@PM.
/// <c>op=send</c> to=pm|@PM body= → latch → CIDE Intercom.
/// <c>op=scene|inbox</c> surfaces unread @PM→@PF ("Message for you, sir!").
/// Agent looks desk, not peek JSON.
/// </summary>
internal static class IdeCideIntercomChannel
{
    public const string ToolName = "cdp_intercom";
    public const string Schema = "cide_intercom/v0";

    public static string HandleJson(IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = Arg(args, "op") ?? "scene";
        return op.Trim().ToLowerInvariant() switch
        {
            "scene" or "get" or "inbox" => Scene(),
            "send" or "say" or "tx" => Send(args),
            "ack" or "read" or "clear" => Ack(args),
            _ => Fail("unknown_op", "op=scene|send|ack  to=pm|pf body=")
        };
    }

    static string Scene()
    {
        var latch = CideIntercomVoiceLatch.TryRead();
        var unread = CideIntercomVoiceLatch.TryUnreadForPf();
        var pulse = CideIntercomVoiceLatch.DeskPulseLine();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "scene",
            role = "cide_intercom",
            seats = new { pf = "agent (v0)", pm = "operator (v0)" },
            latch_path = CideIntercomVoiceLatch.LatchPath,
            pulse,
            unread = unread is null ? null : Card(unread),
            latest = latch is null ? null : Card(latch),
            hint =
                "send to=pm body=… → @PM on CIDE Intercom. " +
                "Operator @PF … → unread here + cockpit pulse. " +
                "ack id= after you read. Meta-roles later via control handoff.",
            next = new object[]
            {
                new { go = "intercom_send", label = "@PM say", why = "to=pm body=…" },
                new { go = "intercom_ack", label = "Ack unread", why = "op=ack" },
                new { go = "intercom", label = "Scene", why = "op=scene" }
            }
        });
    }

    static string Send(IReadOnlyDictionary<string, JsonElement> args)
    {
        var body = Arg(args, "body") ?? Arg(args, "message") ?? Arg(args, "text") ?? Arg(args, "msg");
        if (string.IsNullOrWhiteSpace(body))
            return Fail("body_required", "send to=pm|pf body=…");

        var toRaw = Arg(args, "to") ?? Arg(args, "with") ?? Arg(args, "seat") ?? "pm";
        var to = CideIntercomVoiceLatch.NormalizeSeat(toRaw);
        if (to is null)
            return Fail("to_invalid", "to=pm|pf (or @PM|@PF)");

        // v0: agent speaks as PF unless from= overrides.
        var fromRaw = Arg(args, "from") ?? CideIntercomVoiceLatch.SeatPf;
        var from = CideIntercomVoiceLatch.NormalizeSeat(fromRaw) ?? CideIntercomVoiceLatch.SeatPf;

        var published = CideIntercomVoiceLatch.Publish(
            from,
            to,
            body!,
            CideIntercomVoiceLatch.OriginAgent);
        if (published is null)
            return Fail("publish_failed", "could not write intercom latch");

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "send",
            message = Card(published),
            latch_path = CideIntercomVoiceLatch.LatchPath,
            chat = $"@{to.ToUpperInvariant()}: {TrimChat(published.Body)}",
            hint = "Latch published — CIDE Intercom applies when projector is up."
        });
    }

    static string Ack(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Arg(args, "id");
        var doc = CideIntercomVoiceLatch.Ack(id);
        if (doc is null)
            return Fail("ack_miss", "no matching unread latch (id= optional)");

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "ack",
            message = Card(doc),
            hint = "Unread cleared from cockpit pulse."
        });
    }

    static object Card(CideIntercomVoiceLatch.IntercomVoiceDoc d) => new
    {
        id = d.Id,
        from = d.FromSeat,
        to = d.ToSeat,
        body = d.Body,
        origin = d.Origin,
        acked = d.Acked,
        stamped_utc = d.StampedUtc
    };

    static string TrimChat(string body) =>
        body.Length <= 120 ? body : body[..117] + "…";

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

    static string Fail(string error, string hint) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = false,
            error,
            hint
        });
}
