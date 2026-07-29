#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent SA/alert pulse → CIDE EICAS bar (instant).
/// Writes %LocalAppData%/cdp-mcp/alert-LATEST.json; CIDE projector maps to <c>IEicasFeed</c>.
/// </summary>
internal static class CideAlertLatch
{
    public const string Schema = "cide_alert_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "alert-LATEST.json");

    public static void Publish(IdeAlertChannel.Snap snap)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var level = snap.Level.ToString().ToLowerInvariant();
            var lines = snap.Level == IdeAlertChannel.Level.Clear
                ? Array.Empty<string>()
                : snap.Lines.Where(static l => !string.IsNullOrWhiteSpace(l)).Take(16).ToArray();
            var doc = new AlertLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Level = level,
                Ok = snap.Ok,
                Pulse = snap.Pulse,
                Lines = lines
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

    public static AlertLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<AlertLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class AlertLatchDoc
    {
        public string Schema { get; set; } = CideAlertLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public string Level { get; set; } = "clear";
        public bool Ok { get; set; } = true;
        public string? Pulse { get; set; }
        public string[]? Lines { get; set; }
    }
}
