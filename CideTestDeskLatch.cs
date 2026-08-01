#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent Test-SA pulse → CIDE/Glass SoftOrgan (instant).
/// Writes %LocalAppData%/cdp-mcp/test_desk-LATEST.json; Glass MFD Tests glance + quiet chrome.
/// Live Tests host SSOT = CIDE Avalonia <c>TestsMfdPageView</c>. Green last_run stays silent (Dark Cockpit).
/// </summary>
internal static class CideTestDeskLatch
{
    public const string Schema = "cide_test_desk_latch/v1";
    public const string OriginAgent = "agent";

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

    public static string LatchPath => Path.Combine(StateRoot, "test_desk-LATEST.json");

    public static void Publish(
        bool active,
        string pulse,
        string? verdict,
        int okCount,
        int totalCount,
        int failed,
        int skipped)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var pulseLine = string.IsNullOrWhiteSpace(pulse) ? "test_desk · idle" : pulse.Trim();
            var doc = new TestDeskLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Active = active,
                Pulse = pulseLine,
                Verdict = string.IsNullOrWhiteSpace(verdict) ? null : verdict.Trim(),
                OkCount = okCount,
                TotalCount = totalCount,
                Failed = failed,
                Skipped = skipped,
                ChromeHint = active ? pulseLine : null
            };
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    public static TestDeskLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<TestDeskLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class TestDeskLatchDoc
    {
        public string Schema { get; set; } = CideTestDeskLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Pulse { get; set; }
        public string? Verdict { get; set; }
        public int OkCount { get; set; }
        public int TotalCount { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public string? ChromeHint { get; set; }
    }
}
