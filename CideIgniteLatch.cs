#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent AutoIgnition continuity → CIDE quiet chrome (instant).
/// Writes %LocalAppData%/cdp-mcp/ignite-LATEST.json; CIDE projector paints
/// WorkspaceChromeBand — not EICAS. Idle stays silent (Dark Cockpit).
/// </summary>
internal static class CideIgniteLatch
{
    public const string Schema = "cide_ignite_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "ignite-LATEST.json");

    public static void Publish(bool active, string pulse, int armedCount, int awaitingCount, bool providerBlocked)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var pulseLine = string.IsNullOrWhiteSpace(pulse) ? "ignite · continuity · idle" : pulse.Trim();
            var doc = new IgniteLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Active = active,
                Pulse = pulseLine,
                ArmedCount = armedCount,
                AwaitingCount = awaitingCount,
                ProviderBlocked = providerBlocked,
                // Silent when idle — seats chrome owns packing; ignite only when continuity live.
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

    public static IgniteLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<IgniteLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class IgniteLatchDoc
    {
        public string Schema { get; set; } = CideIgniteLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Pulse { get; set; }
        public int ArmedCount { get; set; }
        public int AwaitingCount { get; set; }
        public bool ProviderBlocked { get; set; }
        public string? ChromeHint { get; set; }
    }
}
