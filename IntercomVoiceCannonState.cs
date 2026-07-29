#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Persistent cannon dedup: one AutoIgnition wake per Intercom msgId across remounts.
/// File: %LocalAppData%/cdp-mcp/intercom-cannon-fired.json
/// </summary>
internal static class IntercomVoiceCannonState
{
    public const string Schema = "intercom_cannon_fired/v0";
    public const int MaxIds = 64;

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

    /// <summary>Test hook — redirect state root (share with latch override).</summary>
    internal static string? RootOverrideForTests
    {
        get => CideIntercomVoiceLatch.RootOverrideForTests;
        set => CideIntercomVoiceLatch.RootOverrideForTests = value;
    }

    public static string StatePath =>
        Path.Combine(CideIntercomVoiceLatch.StateRoot, "intercom-cannon-fired.json");

    public static string ArmIdFor(string msgId) => "intercom-pf-" + msgId;

    /// <summary>True if this msgId already armed/fired the cannon (memory or disk).</summary>
    public static bool WasFired(string msgId)
    {
        if (string.IsNullOrWhiteSpace(msgId))
            return false;
        var id = msgId.Trim();
        var doc = TryRead();
        if (doc?.FiredIds is null || doc.FiredIds.Count == 0)
            return false;
        return doc.FiredIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Record msgId as fired. Returns false if it was already recorded.</summary>
    public static bool TryMarkFired(string msgId)
    {
        if (string.IsNullOrWhiteSpace(msgId))
            return false;
        var id = msgId.Trim();
        try
        {
            Directory.CreateDirectory(CideIntercomVoiceLatch.StateRoot);
            var doc = TryRead() ?? new FiredDoc { Schema = Schema };
            doc.FiredIds ??= [];
            if (doc.FiredIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)))
                return false;

            doc.FiredIds.Add(id);
            while (doc.FiredIds.Count > MaxIds)
                doc.FiredIds.RemoveAt(0);
            doc.LastFiredId = id;
            doc.StampedUtc = DateTimeOffset.UtcNow;

            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = StatePath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, StatePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static FiredDoc? TryRead()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;
            return JsonSerializer.Deserialize<FiredDoc>(File.ReadAllText(StatePath), ReadOpts);
        }
        catch
        {
            return null;
        }
    }

    sealed class FiredDoc
    {
        public string Schema { get; set; } = IntercomVoiceCannonState.Schema;
        public List<string>? FiredIds { get; set; }
        public string? LastFiredId { get; set; }
        public DateTimeOffset? StampedUtc { get; set; }
    }
}
