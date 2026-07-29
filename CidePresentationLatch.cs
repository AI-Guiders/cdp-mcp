#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent desk → CIDE presentation topology (instant glass).
/// Writes %LocalAppData%/cdp-mcp/presentation-LATEST.json; CIDE projector reapplies
/// display.screens.topology live. Internal transport — agent looks desk, not JSON.
/// Does not touch agent <c>cdp_settings</c> desk keys or repo <c>workspace.toml</c>.
/// </summary>
internal static class CidePresentationLatch
{
    public const string Schema = "cide_presentation_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "presentation-LATEST.json");

    public static void Publish(string topology, string origin)
    {
        if (string.IsNullOrWhiteSpace(topology))
            return;
        if (!string.Equals(origin, OriginAgent, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(origin, OriginHuman, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            Directory.CreateDirectory(StateRoot);
            var doc = new PresentationLatchDoc
            {
                Schema = Schema,
                Topology = topology.Trim(),
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
            /* best-effort */
        }
    }

    public static PresentationLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<PresentationLatchDoc>(raw, ReadOpts);
            if (doc is null || string.IsNullOrWhiteSpace(doc.Topology))
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

    public sealed class PresentationLatchDoc
    {
        public string Schema { get; set; } = CidePresentationLatch.Schema;
        public string Topology { get; set; } = "";
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
    }
}
