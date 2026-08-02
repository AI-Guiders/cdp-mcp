#nullable enable
using System.Text.Json;

namespace CdpMcp;
internal static partial class IdeCideIntercomChannel
{
    static object? PresenceCard(CideIntercomPresenceLatch.PresenceDoc? doc)
    {
        if (doc is null)
            return null;
        return new
        {
            schema = doc.Schema,
            pf = SeatCard(doc.Pf),
            pm = SeatCard(doc.Pm)
        };
    }

    static object? SeatCard(CideIntercomPresenceLatch.PresenceSeat? s) => s is null ? null : new
    {
        state = s.State,
        stamped_utc = s.StampedUtc,
        ttl_s = s.TtlSeconds
    };
    static string History(IReadOnlyDictionary<string, JsonElement> args)
    {
        var limit = 20;
        if (args.TryGetValue("limit", out var limEl))
        {
            if (limEl.ValueKind == JsonValueKind.Number && limEl.TryGetInt32(out var n))
                limit = n;
            else if (limEl.ValueKind == JsonValueKind.String && int.TryParse(limEl.GetString(), out var ns))
                limit = ns;
        }

        var entries = CideIntercomVoiceLatch.LoadJournalTail(limit);
        return JsonSerializer.Serialize(new { schema = Schema, ok = true, op = "history", journal_path = CideIntercomVoiceLatch.JournalPath, count = entries.Count, total = CideIntercomVoiceLatch.JournalCount(), entries = entries.Select(Card).ToArray(), hint = entries.Count == 0 ? "Journal empty — send/receive first. Not auto-injected into flight context." : "Virtual History on demand. Pull only what you need; do not dump into composer." });
    }

    static object Card(CideIntercomVoiceLatch.IntercomVoiceDoc d) => new
    {
        id = d.Id,
        from = d.FromSeat,
        to = d.ToSeat,
        body = d.Body,
        origin = d.Origin,
        name = d.Name,
        kind = d.Kind,
        role_label = d.Name is { Length: > 0 } && d.Kind is { Length: > 0 }
            ? CideIntercomVoiceLatch.FormatRoleLabel(d.FromSeat, d.ToSeat, d.Name, d.Kind)
            : null,
        acked = d.Acked,
        stamped_utc = d.StampedUtc
    };
    /// <summary>PF defaults to agent; PM (operator/Who) defaults to human. Explicit origin= wins.</summary>
    static string? ResolveOrigin(string fromSeat, string? originRaw)
    {
        if (!string.IsNullOrWhiteSpace(originRaw))
        {
            var o = originRaw.Trim().ToLowerInvariant();
            if (o is "agent" or "pf" or "pilot")
                return CideIntercomVoiceLatch.OriginAgent;
            if (o is "human" or "operator" or "pm" or "who")
                return CideIntercomVoiceLatch.OriginHuman;
            return null;
        }

        return string.Equals(fromSeat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase) ? CideIntercomVoiceLatch.OriginHuman : CideIntercomVoiceLatch.OriginAgent;
    }

    static string TrimChat(string body) => body.Length <= 120 ? body : body[..117] + "…";
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

    static string Fail(string error, string hint) => JsonSerializer.Serialize(new { schema = Schema, ok = false, error, hint });
}