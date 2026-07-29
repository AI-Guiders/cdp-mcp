#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Durable land pulse for operator GUI projector (ADR-0019 / Start-Stop contract).
/// Agent <c>cdp_land</c> open|goto writes %LocalAppData%/cdp-mcp/land-LATEST.json;
/// CascadeIDE watches and applies <c>open_file</c> + line reveal. Melody untouched.
/// </summary>
internal static class NavigationLandLatch
{
    public const string Schema = "navigation_land_latch/v1";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Test hook: redirect latch root (default LocalAppData/cdp-mcp).</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "land-LATEST.json");

    public static void Publish(string command, string path, int? line, string? member, string? wire)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var doc = new LandLatchDoc
            {
                Schema = Schema,
                Command = command,
                Path = path,
                Line = line is > 0 ? line : null,
                Member = string.IsNullOrWhiteSpace(member) ? null : member,
                Wire = wire,
                StampedUtc = DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
        }
        catch
        {
            /* best-effort — land still succeeds for agent desk */
        }
    }

    public sealed class LandLatchDoc
    {
        public string Schema { get; set; } = NavigationLandLatch.Schema;
        public string Command { get; set; } = "open";
        public string Path { get; set; } = "";
        public int? Line { get; set; }
        public string? Member { get; set; }
        public string? Wire { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
    }
}
