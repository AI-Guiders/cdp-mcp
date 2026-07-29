#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Tool-call wake-on-threshold pulse → %LocalAppData%/cdp-mcp/tool-watch-LATEST.json.
/// CIDE may paint quiet chrome later; agent continuity via Autoi once-wake + result wake=.
/// </summary>
internal static class CideToolWatchLatch
{
    public const string Schema = "cide_tool_watch_latch/v1";
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

    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "tool-watch-LATEST.json");

    public static void Publish(
        bool active,
        string pulse,
        string tool,
        int thresholdSeconds,
        DateTimeOffset startedUtc)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var pulseLine = string.IsNullOrWhiteSpace(pulse) ? "tool-watch · idle" : pulse.Trim();
            var doc = new ToolWatchLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Active = active,
                Pulse = pulseLine,
                Tool = tool,
                ThresholdSeconds = thresholdSeconds,
                StartedUtc = startedUtc,
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

    public static void Clear()
    {
        Publish(active: false, pulse: "tool-watch · idle", tool: "", thresholdSeconds: 0, startedUtc: DateTimeOffset.UtcNow);
    }

    public static ToolWatchLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<ToolWatchLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class ToolWatchLatchDoc
    {
        public string? Schema { get; set; }
        public string? Origin { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Pulse { get; set; }
        public string? Tool { get; set; }
        public int ThresholdSeconds { get; set; }
        public DateTimeOffset StartedUtc { get; set; }
        public string? ChromeHint { get; set; }
    }
}
