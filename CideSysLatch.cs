#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent sys ops pulse → CIDE quiet chrome (instant).
/// Writes %LocalAppData%/cdp-mcp/sys-LATEST.json; CIDE projector paints
/// WorkspaceChromeBand — not EICAS. Clear+idle stays silent (Dark Cockpit).
/// </summary>
internal static class CideSysLatch
{
    public const string Schema = "cide_sys_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "sys-LATEST.json");

    public static void Publish(bool active, string pulse, string? seat, bool pending)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var pulseLine = string.IsNullOrWhiteSpace(pulse) ? "ops · idle" : pulse.Trim();
            var doc = new SysLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Active = active,
                Pulse = pulseLine,
                Seat = string.IsNullOrWhiteSpace(seat) ? null : seat.Trim(),
                Pending = pending,
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

    public static SysLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<SysLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class SysLatchDoc
    {
        public string Schema { get; set; } = CideSysLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Pulse { get; set; }
        public string? Seat { get; set; }
        public bool Pending { get; set; }
        public string? ChromeHint { get; set; }
    }
}
