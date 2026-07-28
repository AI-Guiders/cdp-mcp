#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Read-only human GUI focus pulse (reverse of <see cref="NavigationLandLatch"/>).
/// CascadeIDE writes %LocalAppData%/cdp-mcp/focus-LATEST.json; agent peeks via editor_scene.
/// </summary>
internal static class NavigationFocusLatch
{
    public const string Schema = "navigation_focus_latch/v1";

    static readonly JsonSerializerOptions JsonOpts = new()
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

    public static string LatchPath => Path.Combine(StateRoot, "focus-LATEST.json");

    public static FocusLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<FocusLatchDoc>(raw, JsonOpts);
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

    public static object? PeekForScene()
    {
        var doc = TryRead();
        if (doc is null)
            return null;
        return new
        {
            schema = doc.Schema,
            path = doc.Path,
            line = doc.Line,
            column = doc.Column,
            end_line = doc.EndLine,
            end_column = doc.EndColumn,
            origin = doc.Origin,
            stamped_utc = doc.StampedUtc
        };
    }

    public sealed class FocusLatchDoc
    {
        public string? Schema { get; set; }
        public string? Path { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public int? EndLine { get; set; }
        public int? EndColumn { get; set; }
        public int CaretOffset { get; set; }
        public int SelectionLength { get; set; }
        public string? Origin { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
    }
}
