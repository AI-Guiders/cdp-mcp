#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent eQRH suggest → CIDE EICAS advisory (instant).
/// Writes %LocalAppData%/cdp-mcp/qrh-LATEST.json; CIDE projector maps to <c>IEicasFeed</c> source=qrh.
/// </summary>
internal static class CideQrhLatch
{
    public const string Schema = "cide_qrh_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "qrh-LATEST.json");

    public static void Publish(IdeQrhChannel.Snap snap)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var hotId = snap.Suggest.HotId;
            string? hotTitle = null;
            if (!string.IsNullOrWhiteSpace(hotId))
            {
                hotTitle = IdeQrhChannel.Builtins()
                    .FirstOrDefault(p => string.Equals(p.Id, hotId, StringComparison.OrdinalIgnoreCase))
                    ?.Title;
            }

            var related = snap.Suggest.RelatedIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Where(id => !string.Equals(id, hotId, StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .Select(id =>
                {
                    var page = IdeQrhChannel.Builtins()
                        .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                    return new RelatedPage
                    {
                        Id = id,
                        Title = page?.Title ?? id
                    };
                })
                .ToArray();

            var doc = new QrhLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Ok = snap.Ok,
                Pulse = snap.Pulse,
                HotId = hotId,
                HotTitle = hotTitle,
                Related = related
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

    public static QrhLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<QrhLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class RelatedPage
    {
        public string Id { get; set; } = "";
        public string? Title { get; set; }
    }

    public sealed class QrhLatchDoc
    {
        public string Schema { get; set; } = CideQrhLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Ok { get; set; } = true;
        public string? Pulse { get; set; }
        public string? HotId { get; set; }
        public string? HotTitle { get; set; }
        public RelatedPage[]? Related { get; set; }
    }
}
