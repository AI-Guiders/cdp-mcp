#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Persistent cannon dedup: one AutoIgnition wake per Intercom msgId across remounts
/// and dual-seat processes (live + debug both watch the same latch).
/// File: %LocalAppData%/cdp-mcp/intercom-cannon-fired.json
/// Claim is cross-process via named Mutex (same pattern as WitDB).
/// </summary>
internal static class IntercomVoiceCannonState
{
    public const string Schema = "intercom_cannon_fired/v0";
    public const int MaxIds = 64;
    const string MutexName = @"Local\CdpMcp.IntercomCannon";

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
        try
        {
            using var gate = new CannonFileGate();
            var doc = TryRead();
            if (doc?.FiredIds is null || doc.FiredIds.Count == 0)
                return false;
            return doc.FiredIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Record msgId as fired. Returns false if it was already recorded.</summary>
    public static bool TryMarkFired(string msgId)
    {
        if (string.IsNullOrWhiteSpace(msgId))
            return false;
        var id = msgId.Trim();
        try
        {
            using var gate = new CannonFileGate();
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

    /// <summary>Cross-process gate so live+debug (and zombie remounts) claim once.</summary>
    sealed class CannonFileGate : IDisposable
    {
        readonly Mutex _mutex;
        readonly bool _owned;

        public CannonFileGate()
        {
            // Tests use RootOverride — isolate mutex so parallel test runs do not deadlock each other.
            var name = MutexName;
            if (RootOverrideForTests is { } root)
            {
                var hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToLowerInvariant())))[..16];
                name = $@"Local\CdpMcp.IntercomCannon.{hash}";
            }

            _mutex = new Mutex(initiallyOwned: false, name: name);
            try
            {
                _owned = _mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                _owned = true;
            }

            if (!_owned)
                throw new IOException("Intercom cannon claim busy (dual-seat?) within 5s");
        }

        public void Dispose()
        {
            if (_owned)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { /* not owner */ }
            }

            _mutex.Dispose();
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
