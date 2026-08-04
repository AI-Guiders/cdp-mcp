#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Sticky Intercom Who display name per seat — freeform (name/nick/whatever).
/// Same continuity idea as agent-line: persists while model+habitat match; claim/change anytime.
/// Latch: %LocalAppData%/cdp-mcp/intercom-identity-LATEST.json
/// Not model id / ModelPicker slot. Operator default in code is generic — personal names live here.
/// </summary>
internal static class CideIntercomIdentityLatch
{
    public const string Schema = "cide_intercom_identity_latch/v0";

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

    static readonly object Gate = new();

    /// <summary>Test hook: redirect latch root (shares voice latch override when set).</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? CideIntercomVoiceLatch.RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "intercom-identity-LATEST.json");

    public static IdentityDoc? TryRead()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(LatchPath))
                    return null;
                var raw = File.ReadAllText(LatchPath);
                var doc = JsonSerializer.Deserialize<IdentityDoc>(raw, ReadOpts);
                if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                    return null;
                return doc;
            }
            catch
            {
                return null;
            }
        }
    }

    public static IdentitySeat? TrySeat(string seatRaw)
    {
        var seat = CideIntercomVoiceLatch.NormalizeSeat(seatRaw);
        if (seat is null)
            return null;
        var doc = TryRead();
        if (doc is null)
            return null;
        return GetSeat(doc, seat);
    }

    /// <summary>Claim/replace sticky Who for a seat. Name is freeform (trim; empty refused).</summary>
    public static IdentityDoc? Claim(string seatRaw, string name, string? kind = null)
    {
        var seat = CideIntercomVoiceLatch.NormalizeSeat(seatRaw);
        var trimmed = name.Trim();
        if (seat is null || trimmed.Length == 0)
            return null;

        var kindNorm = CideIntercomVoiceLatch.NormalizeKind(kind);
        if (kindNorm is null)
        {
            kindNorm = string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase)
                ? CideIntercomVoiceLatch.KindOperator
                : CideIntercomVoiceLatch.KindGuest;
        }

        lock (Gate)
        {
            var doc = TryReadUnlocked() ?? new IdentityDoc { Schema = Schema };
            doc.Schema = Schema;
            SetSeat(doc, seat, new IdentitySeat
            {
                Name = trimmed,
                Kind = kindNorm,
                StampedUtc = DateTimeOffset.UtcNow
            });
            return WriteUnlocked(doc) ? doc : null;
        }
    }

    public static IdentityDoc? Clear(string seatRaw)
    {
        var seat = CideIntercomVoiceLatch.NormalizeSeat(seatRaw);
        if (seat is null)
            return null;

        lock (Gate)
        {
            var doc = TryReadUnlocked() ?? new IdentityDoc { Schema = Schema };
            doc.Schema = Schema;
            SetSeat(doc, seat, null);
            return WriteUnlocked(doc) ? doc : null;
        }
    }

    static IdentityDoc? TryReadUnlocked()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<IdentityDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    static bool WriteUnlocked(IdentityDoc doc)
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

    static IdentitySeat? GetSeat(IdentityDoc doc, string seat) =>
        string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase)
            ? doc.Pm
            : doc.Pf;

    static void SetSeat(IdentityDoc doc, string seat, IdentitySeat? slot)
    {
        if (string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase))
            doc.Pm = slot;
        else
            doc.Pf = slot;
    }

    public sealed class IdentityDoc
    {
        public string Schema { get; set; } = CideIntercomIdentityLatch.Schema;
        public IdentitySeat? Pf { get; set; }
        public IdentitySeat? Pm { get; set; }
    }

    public sealed class IdentitySeat
    {
        public string Name { get; set; } = "";
        public string? Kind { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
    }
}
