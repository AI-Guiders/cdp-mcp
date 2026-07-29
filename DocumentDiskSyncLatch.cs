#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Shared dirty glass: Instant Save / human Save pulse for dual-cockpit.
/// Writer publishes %LocalAppData%/cdp-mcp/disk-LATEST.json; peer reloads open buffer/tab
/// so dirty clears on both sides without land. Internal transport — agent surface remains desk.
/// </summary>
internal static class DocumentDiskSyncLatch
{
    public const string Schema = "document_disk_sync_latch/v1";
    public const string OriginAgent = "agent";
    public const string OriginHuman = "human";

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

    public static string LatchPath => Path.Combine(StateRoot, "disk-LATEST.json");

    public static void Publish(string path, string origin)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!string.Equals(origin, OriginAgent, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(origin, OriginHuman, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            Directory.CreateDirectory(StateRoot);
            var doc = new DiskSyncDoc
            {
                Schema = Schema,
                Path = Path.GetFullPath(path),
                Origin = origin.ToLowerInvariant(),
                StampedUtc = DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
        }
        catch
        {
            /* best-effort — flush still succeeds for agent desk */
        }
    }

    public static DiskSyncDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<DiskSyncDoc>(raw, ReadOpts);
            if (doc is null || string.IsNullOrWhiteSpace(doc.Path))
                return null;
            if (!string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class DiskSyncDoc
    {
        public string Schema { get; set; } = DocumentDiskSyncLatch.Schema;
        public string Path { get; set; } = "";
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
    }
}
