#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent ECL checklist → CIDE EICAS advisory (instant).
/// Writes %LocalAppData%/cdp-mcp/ecl-LATEST.json; CIDE projector maps to <c>IEicasFeed</c> source=ecl.
/// </summary>
internal static class CideEclLatch
{
    public const string Schema = "cide_ecl_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "ecl-LATEST.json");

    public static void Publish(IdeChkChannel.Snap snap)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var hotId = snap.HotId;
            string? hotTitle = null;
            IdeChkChannel.RunSnap? hotRun = null;
            if (!string.IsNullOrWhiteSpace(hotId))
            {
                hotRun = snap.Active.FirstOrDefault(r =>
                    string.Equals(r.Id, hotId, StringComparison.OrdinalIgnoreCase));
                hotTitle = hotRun?.Title;
            }

            var openItems = EnumerateOpenTexts(hotRun).Take(4).ToArray();

            var doc = new EclLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Ok = snap.Ok,
                Pulse = snap.Pulse,
                HotId = hotId,
                HotTitle = hotTitle,
                OpenRequired = snap.OpenRequired,
                ActiveCount = snap.ActiveCount,
                OpenItems = openItems
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

    static IEnumerable<OpenItem> EnumerateOpenTexts(IdeChkChannel.RunSnap? run)
    {
        if (run is null)
            yield break;

        foreach (var item in run.MemoryItems.Concat(run.Items))
        {
            if (item.Done || !item.Required)
                continue;
            yield return new OpenItem
            {
                Id = item.Id,
                Text = item.Text
            };
        }
    }

    public static EclLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<EclLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class OpenItem
    {
        public string Id { get; set; } = "";
        public string? Text { get; set; }
    }

    public sealed class EclLatchDoc
    {
        public string Schema { get; set; } = CideEclLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Ok { get; set; } = true;
        public string? Pulse { get; set; }
        public string? HotId { get; set; }
        public string? HotTitle { get; set; }
        public int OpenRequired { get; set; }
        public int ActiveCount { get; set; }
        public OpenItem[]? OpenItems { get; set; }
    }
}
