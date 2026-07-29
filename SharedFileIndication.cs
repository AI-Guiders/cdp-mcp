#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Dual-cockpit co-presence: human focus path ∩ agent open buffers.
/// Desk marks sit.locus with " · shared"; latch is internal feed (not GetEditorState).
/// </summary>
internal static class SharedFileIndication
{
    public const string Schema = "shared_file_latch/v1";
    public const string SharedSuffix = " · shared";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "shared-LATEST.json");

    public static bool PathsReferToSameFile(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a),
                Path.GetFullPath(b),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsShared(string? humanPath, IEnumerable<string?> agentOpenPaths)
    {
        if (string.IsNullOrWhiteSpace(humanPath))
            return false;
        foreach (var p in agentOpenPaths)
        {
            if (PathsReferToSameFile(humanPath, p))
                return true;
        }

        return false;
    }

    public static void Publish(string? path, bool shared)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var doc = new SharedFileDoc
            {
                Schema = Schema,
                Path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path),
                Shared = shared,
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

    public sealed class SharedFileDoc
    {
        public string Schema { get; set; } = SharedFileIndication.Schema;
        public string? Path { get; set; }
        public bool Shared { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
    }
}
