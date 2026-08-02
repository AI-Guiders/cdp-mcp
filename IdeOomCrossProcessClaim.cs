#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Cross-process OOM wake claim — dual-seat / zombie remounts all poll the
/// native OOM dialog + CDT recover edge; only one process may schedule oom-wake
/// within <see cref="IdeOomWake.WakeCooldown"/> (dogfood: twin wake_schedule
/// oom_dialog → twin no_agent_composer).
/// File: %LocalAppData%/cdp-mcp/oom-wake-claim.json — Mutex like HILD claim.
/// </summary>
internal static class IdeOomCrossProcessClaim
{
    public const string Schema = "oom_wake_claim/v0";
    const string MutexName = @"Local\CdpMcp.OomWakeClaim";

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

    /// <summary>Test hook — redirect state root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string StatePath => Path.Combine(StateRoot, "oom-wake-claim.json");

    /// <summary>
    /// True if this process may schedule one oom-wake now.
    /// Fail-closed on IO (prefer miss over twin inject).
    /// </summary>
    public static bool TryClaimSchedule(TimeSpan cooldown)
    {
        try
        {
            using var gate = new ClaimFileGate();
            Directory.CreateDirectory(StateRoot);
            var now = DateTimeOffset.UtcNow;
            var doc = TryRead() ?? new ClaimDoc { Schema = Schema };
            if (doc.ClaimedUtc is { } last && (now - last) < cooldown)
                return false;

            doc.Schema = Schema;
            doc.ClaimedUtc = now;
            doc.ClaimerPid = Environment.ProcessId;
            Write(doc);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void Write(ClaimDoc doc)
    {
        var json = JsonSerializer.Serialize(doc, JsonOpts);
        var tmp = StatePath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, StatePath, overwrite: true);
    }

    static ClaimDoc? TryRead()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;
            return JsonSerializer.Deserialize<ClaimDoc>(File.ReadAllText(StatePath), ReadOpts);
        }
        catch
        {
            return null;
        }
    }

    sealed class ClaimFileGate : IDisposable
    {
        readonly Mutex _mutex;
        readonly bool _owned;

        public ClaimFileGate()
        {
            var name = MutexName;
            if (RootOverrideForTests is { } root)
            {
                var hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToLowerInvariant())))[..16];
                name = $@"Local\CdpMcp.OomWakeClaim.{hash}";
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
                throw new IOException("OOM wake claim busy within 5s");
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

    sealed class ClaimDoc
    {
        public string Schema { get; set; } = IdeOomCrossProcessClaim.Schema;
        public DateTimeOffset? ClaimedUtc { get; set; }
        public int? ClaimerPid { get; set; }
    }
}
