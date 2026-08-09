#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Sticky Intercom Who — freeform nick per seat, keyed by model slot.
/// Slot (model id) ≠ personality (Who): switch model → do not inherit prior nick;
/// same model returns → prior Who restores from profiles.
/// Latch: %LocalAppData%/cdp-mcp/intercom-identity-LATEST.json
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

    /// <summary>Tip Who for seat (after last Activate/Claim) — may be null after model switch.</summary>
    public static IdentitySeat? TrySeat(string seatRaw)
    {
        var seat = CideIntercomVoiceLatch.NormalizeSeat(seatRaw);
        if (seat is null)
            return null;
        var doc = TryRead();
        if (doc is null)
            return null;
        return GetTip(doc, seat);
    }

    /// <summary>
    /// Face Who for FM model (citizen Radio/busy). Tip plane ≠ Face:
    /// harness guest/operator tip (Cursor PF) is preserved — do not promote Face onto tip.
    /// Guest/operator under an FM model id are pollution — scrub and treat as missing.
    /// </summary>
    public static IdentitySeat? Activate(string seatRaw, string? model)
    {
        var seat = CideIntercomVoiceLatch.NormalizeSeat(seatRaw);
        var modelKey = NormModel(model);
        if (seat is null || modelKey.Length == 0)
            return null;

        lock (Gate)
        {
            var doc = TryReadUnlocked() ?? new IdentityDoc { Schema = Schema };
            doc.Schema = Schema;
            MigrateLegacyTip(doc, seat, modelKey);

            var tipNow = GetTip(doc, seat);
            var tipIsHarness = TipIsHarness(tipNow);

            var profiles = GetProfiles(doc, seat);
            if (profiles.TryGetValue(modelKey, out var profile) && profile.Name.Length > 0)
            {
                var pk = CideIntercomVoiceLatch.NormalizeKind(profile.Kind);
                if (!IsHarnessSlot(modelKey)
                    && (string.Equals(pk, CideIntercomVoiceLatch.KindGuest, StringComparison.Ordinal)
                        || string.Equals(pk, CideIntercomVoiceLatch.KindOperator, StringComparison.Ordinal)))
                {
                    profiles.Remove(modelKey);
                    SetProfiles(doc, seat, profiles);
                    if (!tipIsHarness)
                        SetTip(doc, seat, null);
                    _ = WriteUnlocked(doc);
                    return null;
                }

                var face = CloneSeat(profile);
                face.Model = modelKey;
                face.StampedUtc = DateTimeOffset.UtcNow;
                // Multi-principal: Cursor tip (harness) survives Face Activate.
                if (!tipIsHarness)
                {
                    SetTip(doc, seat, face);
                    return WriteUnlocked(doc) ? face : null;
                }

                return WriteUnlocked(doc) ? face : null;
            }

            // Model switch with no Face profile — clear citizen tip only; keep harness Cursor tip.
            if (!tipIsHarness)
                SetTip(doc, seat, null);
            return WriteUnlocked(doc) ? null : null;
        }
    }


    /// <summary>Cursor/external guest Who — never a citizen FM model id.</summary>
    public const string HarnessGuestSlot = "harness:guest";

    /// <summary>Operator Who profile — never a citizen FM model id.</summary>
    public const string HarnessOperatorSlot = "harness:operator";

    /// <summary>
    /// Profile key: guest/operator → harness slots; citizen → FM model id.
    /// Seat tip may still paint last Claim; citizen Activate restores from FM profile.
    /// </summary>
    public static string ProfileSlotFor(string? kindRaw, string? model)
    {
        var kind = CideIntercomVoiceLatch.NormalizeKind(kindRaw) ?? CideIntercomVoiceLatch.KindGuest;
        if (string.Equals(kind, CideIntercomVoiceLatch.KindGuest, StringComparison.Ordinal))
            return HarnessGuestSlot;
        if (string.Equals(kind, CideIntercomVoiceLatch.KindOperator, StringComparison.Ordinal))
            return HarnessOperatorSlot;

        var modelKey = NormModel(model);
        if (modelKey.Length == 0)
            modelKey = NormModel(CitizenIdentity.ResolveCitizenModel());
        return modelKey;
    }

    public static bool IsHarnessSlot(string? modelKey)
    {
        var key = NormModel(modelKey);
        return key.Length > 0
            && (string.Equals(key, HarnessGuestSlot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, HarnessOperatorSlot, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Claim Who under kind-scoped profile. Tip=Cursor; citizen Face does not stomp harness tip.</summary>
    public static IdentityDoc? Claim(string seatRaw, string name, string? kind = null, string? model = null)
    {
        var seat = CideIntercomVoiceLatch.NormalizeSeat(seatRaw);
        var trimmed = name.Trim();
        if (seat is null || trimmed.Length == 0)
            return null;
        // Autoi remount Publish(name=AutoI) must not overwrite Sierra / citizen Who.
        if (CideIntercomVoiceLatch.IsSystemVoiceWho(trimmed))
            return null;

        var kindNorm = CideIntercomVoiceLatch.NormalizeKind(kind);
        if (kindNorm is null)
        {
            kindNorm = string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase)
                ? CideIntercomVoiceLatch.KindOperator
                : CideIntercomVoiceLatch.KindGuest;
        }

        // PM seat is operator standing — guest Claim is role slip, not Who.
        if (string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase)
            && string.Equals(kindNorm, CideIntercomVoiceLatch.KindGuest, StringComparison.Ordinal))
            kindNorm = CideIntercomVoiceLatch.KindOperator;

        var modelKey = ProfileSlotFor(kindNorm, model);
        if (modelKey.Length == 0)
            return null;

        lock (Gate)
        {
            var doc = TryReadUnlocked() ?? new IdentityDoc { Schema = Schema };
            doc.Schema = Schema;

            var entry = new IdentitySeat
            {
                Name = trimmed,
                Kind = kindNorm,
                Model = modelKey,
                StampedUtc = DateTimeOffset.UtcNow
            };
            var profiles = GetProfiles(doc, seat);
            profiles[modelKey] = CloneSeat(entry);
            SetProfiles(doc, seat, profiles);

            // Tip = Cursor Who. Citizen Claim writes Face profile only when harness tip already stands.
            var tipIsHarness = TipIsHarness(GetTip(doc, seat));
            var claimIsHarness = IsHarnessSlot(modelKey)
                || string.Equals(kindNorm, CideIntercomVoiceLatch.KindGuest, StringComparison.Ordinal)
                || string.Equals(kindNorm, CideIntercomVoiceLatch.KindOperator, StringComparison.Ordinal);
            if (claimIsHarness || !tipIsHarness)
                SetTip(doc, seat, entry);

            return WriteUnlocked(doc) ? doc : null;
        }
    }

    /// <summary>Cursor tip = guest|operator on harness slot (not guest pollution under FM model id).</summary>
    static bool TipIsHarness(IdentitySeat? tip)
    {
        if (tip is null || tip.Name.Length == 0)
            return false;
        var k = CideIntercomVoiceLatch.NormalizeKind(tip.Kind);
        if (!string.Equals(k, CideIntercomVoiceLatch.KindGuest, StringComparison.Ordinal)
            && !string.Equals(k, CideIntercomVoiceLatch.KindOperator, StringComparison.Ordinal))
            return false;
        // Pollution: guest under FM model id is not Cursor tip — Activate may scrub it.
        if (!string.IsNullOrWhiteSpace(tip.Model) && !IsHarnessSlot(tip.Model))
            return false;
        return true;
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
            SetTip(doc, seat, null);
            SetProfiles(doc, seat, new Dictionary<string, IdentitySeat>(StringComparer.OrdinalIgnoreCase));
            return WriteUnlocked(doc) ? doc : null;
        }
    }

    static void MigrateLegacyTip(IdentityDoc doc, string seat, string modelKey)
    {
        var tip = GetTip(doc, seat);
        if (tip is null || tip.Name.Length == 0)
            return;
        if (!string.IsNullOrWhiteSpace(tip.Model))
            return;

        tip.Model = modelKey;
        tip.StampedUtc = DateTimeOffset.UtcNow;
        var profiles = GetProfiles(doc, seat);
        profiles[modelKey] = CloneSeat(tip);
        SetProfiles(doc, seat, profiles);
        SetTip(doc, seat, tip);
    }

    static string NormModel(string? model) =>
        string.IsNullOrWhiteSpace(model) ? "" : model.Trim();

    static IdentitySeat CloneSeat(IdentitySeat s) => new()
    {
        Name = s.Name,
        Kind = s.Kind,
        Model = s.Model,
        StampedUtc = s.StampedUtc
    };

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

    static IdentitySeat? GetTip(IdentityDoc doc, string seat) =>
        string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase)
            ? doc.Pm
            : doc.Pf;

    static void SetTip(IdentityDoc doc, string seat, IdentitySeat? slot)
    {
        if (string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase))
            doc.Pm = slot;
        else
            doc.Pf = slot;
    }

    static Dictionary<string, IdentitySeat> GetProfiles(IdentityDoc doc, string seat)
    {
        var raw = string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase)
            ? doc.PmProfiles
            : doc.PfProfiles;
        if (raw is null)
            return new Dictionary<string, IdentitySeat>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, IdentitySeat>(raw, StringComparer.OrdinalIgnoreCase);
    }

    static void SetProfiles(IdentityDoc doc, string seat, Dictionary<string, IdentitySeat> profiles)
    {
        if (string.Equals(seat, CideIntercomVoiceLatch.SeatPm, StringComparison.OrdinalIgnoreCase))
            doc.PmProfiles = profiles.Count == 0 ? null : profiles;
        else
            doc.PfProfiles = profiles.Count == 0 ? null : profiles;
    }

    public sealed class IdentityDoc
    {
        public string Schema { get; set; } = CideIntercomIdentityLatch.Schema;
        public IdentitySeat? Pf { get; set; }
        public IdentitySeat? Pm { get; set; }
        public Dictionary<string, IdentitySeat>? PfProfiles { get; set; }
        public Dictionary<string, IdentitySeat>? PmProfiles { get; set; }
    }

    public sealed class IdentitySeat
    {
        public string Name { get; set; } = "";
        public string? Kind { get; set; }
        /// <summary>Model slot this Who is bound to (Cloud.ru / Anthropic id).</summary>
        public string? Model { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
    }
}
