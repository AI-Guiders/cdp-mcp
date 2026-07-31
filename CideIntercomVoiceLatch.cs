#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Dual-cockpit Intercom voice (PF/PM). Internal transport only —
/// agent looks <c>cdp_intercom</c> / cockpit pulse, not JSON peek APIs.
/// Latch: %LocalAppData%/cdp-mcp/intercom-LATEST.json
/// v0 seats: agent=PF, operator=PM (meta-roles later via control handoff).
/// </summary>
internal static partial class CideIntercomVoiceLatch
{
    public const string Schema = "cide_intercom_voice_latch/v0";
    public const string OriginAgent = "agent";
    public const string OriginHuman = "human";
    public const string SeatPf = "pf";
    public const string SeatPm = "pm";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "intercom-LATEST.json");

    public static IntercomVoiceDoc? Publish(
        string fromSeat,
        string toSeat,
        string body,
        string origin,
        string? id = null)
    {
        var from = NormalizeSeat(fromSeat);
        var to = NormalizeSeat(toSeat);
        var trimmed = body.Trim();
        if (from is null || to is null || trimmed.Length == 0)
            return null;
        if (!string.Equals(origin, OriginAgent, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(origin, OriginHuman, StringComparison.OrdinalIgnoreCase))
            return null;

        var doc = new IntercomVoiceDoc
        {
            Schema = Schema,
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..12] : id.Trim(),
            FromSeat = from,
            ToSeat = to,
            Body = trimmed,
            Origin = origin.ToLowerInvariant(),
            StampedUtc = DateTimeOffset.UtcNow,
            Acked = false
        };

        try
        {
            Directory.CreateDirectory(StateRoot);
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
            AppendJournal(doc);
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public static IntercomVoiceDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<IntercomVoiceDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.IsNullOrWhiteSpace(doc.Body))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Unread for PF = human→pf and not acked.</summary>
    public static IntercomVoiceDoc? TryUnreadForPf()
    {
        var doc = TryRead();
        if (doc is null || doc.Acked)
            return null;
        if (!string.Equals(doc.ToSeat, SeatPf, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!string.Equals(doc.Origin, OriginHuman, StringComparison.OrdinalIgnoreCase))
            return null;
        return doc;
    }

    public static IntercomVoiceDoc? Ack(string? id = null)
    {
        var doc = TryRead();
        if (doc is null)
            return null;
        if (id is { Length: > 0 }
            && !string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase))
            return null;

        doc.Acked = true;
        try
        {
            Directory.CreateDirectory(StateRoot);
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
            return doc;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Desk pulse line — Monty Python cannon cue.</summary>
    public static string? DeskPulseLine()
    {
        var unread = TryUnreadForPf();
        if (unread is null)
            return null;
        var body = unread.Body;
        if (body.Length > 80)
            body = body[..77] + "…";
        return $"Message for you, sir! @PM: {body}";
    }

    public static string? NormalizeSeat(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var s = raw.Trim().TrimStart('@').ToLowerInvariant();
        return s switch
        {
            "pf" or "pilot_flying" or "pilot-flying" or "agent" => SeatPf,
            "pm" or "pilot_monitoring" or "pilot-monitoring" or "operator" or "human" => SeatPm,
            _ => null
        };
    }

    public sealed class IntercomVoiceDoc
    {
        public string Schema { get; set; } = CideIntercomVoiceLatch.Schema;
        public string Id { get; set; } = "";
        public string FromSeat { get; set; } = SeatPf;
        public string ToSeat { get; set; } = SeatPm;
        public string Body { get; set; } = "";
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Acked { get; set; }
    }
}
