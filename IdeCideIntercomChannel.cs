#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Desk Intercom voice — dual-cockpit @PF/@PM.
/// <c>op=send</c> to=pm|@PM body= → latch → CIDE Intercom.
/// <c>op=scene|inbox</c> surfaces unread @PM→@PF ("Message for you, sir!").
/// <c>op=history|line</c> — Virtual History journal (PF on-demand, not auto into flight).
/// Agent looks desk, not peek JSON.
/// </summary>
internal static partial class IdeCideIntercomChannel
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
            "history" or "line" or "journal" or "tail" => History(args),
            "presence" or "status" or "pulse_presence" => Presence(args),
            _ => Fail("unknown_op", "op=scene|send|ack|history|presence  to=pm|pf body= | seat= state=")
        };
    }

    static string Scene()
    {
        var latch = CideIntercomVoiceLatch.TryRead();
        var unread = CideIntercomVoiceLatch.TryUnreadForPf();
        var pulse = CideIntercomVoiceLatch.DeskPulseLine();
        var journalCount = CideIntercomVoiceLatch.JournalCount();
        var presence = CideIntercomPresenceLatch.TryReadEffective();
        var partnerPresence = CideIntercomPresenceLatch.PartnerLine(CideIntercomVoiceLatch.SeatPf, presence);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "scene",
            role = "cide_intercom",
            seats = new
            {
                pf = "Кир · guest (v0 Cursor PF)",
                pm = "Who · operator (v0)",
                kinds = new[] { "guest", "citizen", "operator" }
            },
            latch_path = CideIntercomVoiceLatch.LatchPath,
            journal_path = CideIntercomVoiceLatch.JournalPath,
            journal_count = journalCount,
            presence_path = CideIntercomPresenceLatch.LatchPath,
            presence = PresenceCard(presence),
            partner_presence = partnerPresence,
            pulse,
            unread = unread is null ? null : Card(unread),
            latest = latch is null ? null : Card(latch),
            hint =
                "send to=pm body=… [name=Кир] [kind=guest|citizen] → @PM on Glass Intercom. " +
                "from=pm|operator body=… [name=Who] → operator voice (origin=human). " +
                "presence seat=pf|pm state=idle|composing|busy — partner observability (no thinking dump). " +
                "history limit= — Virtual History on demand (not auto into flight). " +
                "ack id= after you read.",
            next = new object[]
            {
                new { go = "intercom_send", label = "@PM say", why = "to=pm body=…" },
                new { go = "intercom_presence", label = "Presence", why = "op=presence seat=pf state=busy" },
                new { go = "intercom_ack", label = "Ack unread", why = "op=ack" },
                new { go = "intercom", label = "History", why = "op=history limit=20" },
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

        // v0: agent speaks as PF unless from= overrides. Origin follows seat:
        // PF→agent, PM→human (Who-as-Operator / Glass PM voice).
        var fromRaw = Arg(args, "from") ?? CideIntercomVoiceLatch.SeatPf;
        var from = CideIntercomVoiceLatch.NormalizeSeat(fromRaw) ?? CideIntercomVoiceLatch.SeatPf;

        var originRaw = Arg(args, "origin");
        var origin = ResolveOrigin(from, originRaw);
        if (origin is null)
            return Fail("origin_invalid", "origin=agent|human (default: pf→agent, pm→human)");

        var name = Arg(args, "name") ?? Arg(args, "display_name") ?? Arg(args, "as");
        var kind = Arg(args, "kind") ?? Arg(args, "role");
        if (kind is not null && CideIntercomVoiceLatch.NormalizeKind(kind) is null)
            return Fail("kind_invalid", "kind=guest|citizen|operator");

        var published = CideIntercomVoiceLatch.Publish(
            from,
            to,
            body!,
            origin,
            name: name,
            kind: kind);
        if (published is null)
            return Fail("publish_failed", "could not write intercom latch");

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "send",
            message = Card(published),
            latch_path = CideIntercomVoiceLatch.LatchPath,
            journal_path = CideIntercomVoiceLatch.JournalPath,
            chat = $"{published.Name ?? "@" + to.ToUpperInvariant()}: {TrimChat(published.Body)}",
            hint = "Latch + journal published — Glass shows name · kind (not model id)."
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

    static string Presence(IReadOnlyDictionary<string, JsonElement> args)
    {
        var seatRaw = Arg(args, "seat") ?? Arg(args, "from") ?? Arg(args, "who") ?? CideIntercomVoiceLatch.SeatPf;
        var stateRaw = Arg(args, "state") ?? Arg(args, "status") ?? Arg(args, "presence");
        if (string.IsNullOrWhiteSpace(stateRaw))
            return Fail("state_required", "presence seat=pf|pm state=idle|composing|busy");

        int? ttl = null;
        if (args.TryGetValue("ttl_s", out var ttlEl)
            && ttlEl.ValueKind == JsonValueKind.Number
            && ttlEl.TryGetInt32(out var ttlN))
            ttl = ttlN;
        else if (args.TryGetValue("ttl", out var ttlAlias)
                 && ttlAlias.ValueKind == JsonValueKind.Number
                 && ttlAlias.TryGetInt32(out var ttlA))
            ttl = ttlA;

        var doc = CideIntercomPresenceLatch.PublishSeat(seatRaw, stateRaw!, ttl);
        if (doc is null)
            return Fail("presence_failed", "seat=pf|pm state=idle|composing|busy (not stale — stale is reader-side)");

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "presence",
            presence_path = CideIntercomPresenceLatch.LatchPath,
            presence = PresenceCard(doc),
            partner_for_glass = CideIntercomPresenceLatch.PartnerLine(CideIntercomVoiceLatch.SeatPm, doc),
            partner_for_agent = CideIntercomPresenceLatch.PartnerLine(CideIntercomVoiceLatch.SeatPf, doc),
            hint = "Presence latch updated — Glass paints partner on IntercomSubtitle; no journal / no thinking dump."
        });
    }

}
