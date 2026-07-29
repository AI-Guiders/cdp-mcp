#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent learn journal pulse → CIDE quiet chrome (instant).
/// Writes %LocalAppData%/cdp-mcp/learn-LATEST.json; CIDE projector paints
/// WorkspaceChromeBand — not EICAS. Empty journal stays silent (Dark Cockpit).
/// </summary>
internal static class CideLearnLatch
{
    public const string Schema = "cide_learn_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "learn-LATEST.json");

    public static void Publish(bool active, string pulse, int cardCount)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var pulseLine = string.IsNullOrWhiteSpace(pulse) ? "learn · idle" : pulse.Trim();
            var doc = new LearnLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Active = active,
                Pulse = pulseLine,
                CardCount = cardCount,
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

    public static LearnLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<LearnLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class LearnLatchDoc
    {
        public string Schema { get; set; } = CideLearnLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Pulse { get; set; }
        public int CardCount { get; set; }
        public string? ChromeHint { get; set; }
    }
}
