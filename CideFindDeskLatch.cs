#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent Find desk pulse → CIDE/Glass SoftOrgan quiet chrome (instant).
/// Writes %LocalAppData%/cdp-mcp/find_desk-LATEST.json.
/// SoftOrganMfdGlance RelatedFiles stays ←refactor (1:1 MFD map; search ≠ debt/blast).
/// Idle (clear) stays silent (Dark Cockpit).
/// </summary>
internal static class CideFindDeskLatch
{
    public const string Schema = "cide_find_desk_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "find_desk-LATEST.json");

    public static void Publish(
        bool active,
        string pulse,
        string? op,
        string? where,
        string? query,
        int hitCount)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var pulseLine = string.IsNullOrWhiteSpace(pulse) ? "find_desk · idle" : pulse.Trim();
            var doc = new FindDeskLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Active = active,
                Pulse = pulseLine,
                Op = string.IsNullOrWhiteSpace(op) ? null : op.Trim(),
                Where = string.IsNullOrWhiteSpace(where) ? null : where.Trim(),
                Query = string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                HitCount = hitCount,
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

    public static FindDeskLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<FindDeskLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class FindDeskLatchDoc
    {
        public string Schema { get; set; } = CideFindDeskLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Pulse { get; set; }
        public string? Op { get; set; }
        public string? Where { get; set; }
        public string? Query { get; set; }
        public int HitCount { get; set; }
        public string? ChromeHint { get; set; }
    }
}
