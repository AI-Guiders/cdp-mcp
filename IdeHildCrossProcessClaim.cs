#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Cross-process HILD claim — zombie remounts / dual-seat all poll Composer;
/// only one process may seed human_away wake and escalate charge (dogfood 16× CdpMcp).
/// File: %LocalAppData%/cdp-mcp/hild-away-claim.json — Mutex like Intercom cannon.
/// </summary>
internal static class IdeHildCrossProcessClaim
{
    public const string Schema = "hild_away_claim/v0";
    const string MutexName = @"Local\CdpMcp.HildAwayClaim";

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

    public static string StatePath => Path.Combine(StateRoot, "hild-away-claim.json");

    /// <summary>True if this process owns the human_away edge (seed wake). Fail-closed on IO.</summary>
    public static bool TryClaimAwayEdge()
    {
        try
        {
            using var gate = new ClaimFileGate();
            Directory.CreateDirectory(StateRoot);
            var doc = TryRead() ?? new ClaimDoc { Schema = Schema };
            if (doc.EdgeClaimed && !doc.Cleared)
                return false;

            doc.Schema = Schema;
            doc.EdgeClaimed = true;
            doc.EscalateClaimed = false;
            doc.Cleared = false;
            doc.EdgeUtc = DateTimeOffset.UtcNow;
            doc.EscalateUtc = null;
            doc.ClaimerPid = Environment.ProcessId;
            Write(doc);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True if this process owns escalate wake.
    /// Fail-open on IO (autonomy must still get a charge).
    /// </summary>
    public static bool TryClaimEscalate()
    {
        try
        {
            using var gate = new ClaimFileGate();
            Directory.CreateDirectory(StateRoot);
            var doc = TryRead() ?? new ClaimDoc { Schema = Schema };
            if (doc.EscalateClaimed && !doc.Cleared)
                return false;

            // Peer may have missed edge claim — still allow one escalate for this absence.
            doc.Schema = Schema;
            doc.EdgeClaimed = true;
            doc.EscalateClaimed = true;
            doc.Cleared = false;
            doc.EscalateUtc = DateTimeOffset.UtcNow;
            doc.EdgeUtc ??= doc.EscalateUtc;
            doc.ClaimerPid = Environment.ProcessId;
            Write(doc);
            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Partner returned — clear so next absence can claim.</summary>
    public static void ClearAwayEpoch()
    {
        try
        {
            using var gate = new ClaimFileGate();
            Directory.CreateDirectory(StateRoot);
            Write(new ClaimDoc
            {
                Schema = Schema,
                Cleared = true,
                EdgeClaimed = false,
                EscalateClaimed = false,
                ClearedUtc = DateTimeOffset.UtcNow,
                ClaimerPid = Environment.ProcessId
            });
        }
        catch
        {
            /* best-effort */
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
                name = $@"Local\CdpMcp.HildAwayClaim.{hash}";
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
                throw new IOException("HILD away claim busy within 5s");
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
        public string Schema { get; set; } = IdeHildCrossProcessClaim.Schema;
        public bool EdgeClaimed { get; set; }
        public bool EscalateClaimed { get; set; }
        public bool Cleared { get; set; }
        public DateTimeOffset? EdgeUtc { get; set; }
        public DateTimeOffset? EscalateUtc { get; set; }
        public DateTimeOffset? ClearedUtc { get; set; }
        public int? ClaimerPid { get; set; }
    }
}
