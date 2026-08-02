#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Dual-seat Intercom partner presence (observability) — not voice, not SoftOrgan/EICAS.
/// Latch: %LocalAppData%/cdp-mcp/intercom-presence-LATEST.json
/// Coarse states only: idle|composing|busy (+ reader-side stale). No thinking/stream dump.
/// </summary>
internal static class CideIntercomPresenceLatch
{
    public const string Schema = "cide_intercom_presence_latch/v0";
    public const string StateIdle = "idle";
    public const string StateComposing = "composing";
    public const string StateBusy = "busy";
    public const string StateStale = "stale";

    public const int DefaultComposingTtlSeconds = 20;
    public const int DefaultBusyTtlSeconds = 120;

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

    /// <summary>Test hook: redirect latch root (shares voice latch override when set).</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? CideIntercomVoiceLatch.RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "intercom-presence-LATEST.json");

    public static string? NormalizeState(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "idle" or "clear" or "ready" => StateIdle,
            "composing" or "typing" or "draft" => StateComposing,
            "busy" or "working" or "generating" or "tools" => StateBusy,
            "stale" => StateStale,
            _ => null
        };
    }

    public static int DefaultTtlSeconds(string state) => state switch
    {
        StateComposing => DefaultComposingTtlSeconds,
        StateBusy => DefaultBusyTtlSeconds,
        _ => 0
    };

    /// <summary>Merge one seat into dual-seat map. Skips rewrite when state unchanged and stamp fresh (&lt;2s).</summary>
    public static PresenceDoc? PublishSeat(string seatRaw, string stateRaw, int? ttlSeconds = null)
    {
        var seat = CideIntercomVoiceLatch.NormalizeSeat(seatRaw);
        var state = NormalizeState(stateRaw);
        if (seat is null || state is null || state == StateStale)
            return null;

        var ttl = ttlSeconds ?? DefaultTtlSeconds(state);
        if (ttl < 0)
            ttl = 0;

        var now = DateTimeOffset.UtcNow;
        var doc = TryReadRaw() ?? new PresenceDoc { Schema = Schema };
        doc.Schema = Schema;

        var existing = GetSeat(doc, seat);
        if (existing is not null
            && string.Equals(existing.State, state, StringComparison.OrdinalIgnoreCase)
            && (now - existing.StampedUtc).TotalSeconds < 2)
        {
            return doc;
        }

        var slot = new PresenceSeat
        {
            State = state,
            StampedUtc = now,
            TtlSeconds = ttl > 0 ? ttl : null
        };
        SetSeat(doc, seat, slot);

        return Write(doc) ? doc : null;
    }

    public static PresenceDoc? TryReadRaw()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<PresenceDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reader-side stale: if stamp+ttl elapsed → paint as stale (does not rewrite).</summary>
    public static PresenceDoc? TryReadEffective(DateTimeOffset? nowUtc = null)
    {
        var doc = TryReadRaw();
        if (doc is null)
            return null;

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        ApplyEffective(doc.Pf, now);
        ApplyEffective(doc.Pm, now);
        return doc;
    }

    static void ApplyEffective(PresenceSeat? seat, DateTimeOffset now)
    {
        if (seat is null)
            return;
        if (string.Equals(seat.State, StateIdle, StringComparison.OrdinalIgnoreCase))
            return;
        var ttl = seat.TtlSeconds ?? DefaultTtlSeconds(seat.State);
        if (ttl <= 0)
            return;
        if ((now - seat.StampedUtc).TotalSeconds > ttl)
            seat.State = StateStale;
    }

    public static PresenceSeat? GetSeat(PresenceDoc doc, string seat) =>
        string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase)
            ? doc.Pm
            : doc.Pf;

    static void SetSeat(PresenceDoc doc, string seat, PresenceSeat slot)
    {
        if (string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase))
            doc.Pm = slot;
        else
            doc.Pf = slot;
    }

    /// <summary>
    /// Citizen @frame line — both seats, coarse states (incl. idle). Null when latch missing.
    /// </summary>
    public static string? AfferentLine(DateTimeOffset? nowUtc = null)
    {
        var doc = TryReadEffective(nowUtc);
        if (doc is null)
            return null;

        static string Label(PresenceSeat? seat) =>
            seat is null || string.IsNullOrWhiteSpace(seat.State)
                ? StateIdle
                : seat.State.Trim().ToLowerInvariant();

        return "presence | @PF " + Label(doc.Pf) + " · @PM " + Label(doc.Pm);
    }

    /// <summary>Glass (PM) watches partner PF; agent desk watches partner PM.</summary>
    public static string? PartnerLine(string viewerSeat, PresenceDoc? doc = null)
    {
        doc ??= TryReadEffective();
        if (doc is null)
            return null;

        var partnerSeat = string.Equals(viewerSeat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase)
            ? CideIntercomVoiceLatch.SeatPf
            : CideIntercomVoiceLatch.SeatPm;
        var slot = GetSeat(doc, partnerSeat);
        if (slot is null || string.IsNullOrWhiteSpace(slot.State))
            return null;
        if (string.Equals(slot.State, StateIdle, StringComparison.OrdinalIgnoreCase))
            return null;

        return $"@{partnerSeat.ToUpperInvariant()} · {slot.State}";
    }

    static bool Write(PresenceDoc doc)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public sealed class PresenceDoc
    {
        public string Schema { get; set; } = CideIntercomPresenceLatch.Schema;
        public PresenceSeat? Pf { get; set; }
        public PresenceSeat? Pm { get; set; }
    }

    public sealed class PresenceSeat
    {
        public string State { get; set; } = StateIdle;
        public DateTimeOffset StampedUtc { get; set; }
        public int? TtlSeconds { get; set; }
    }
}
