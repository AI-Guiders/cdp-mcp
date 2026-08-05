#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Latch after successful git commit — cockpit next[] nudges stamp domain (anti-rooster).
/// Cleared when <see cref="IdeDomainStampShield"/> accepts a fresh stamp.
/// </summary>
internal static class IdeDomainStampPending
{
    internal static string? PathOverrideForTests { get; set; }

    static string FilePath =>
        PathOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "domain-stamp-pending.json");

    /// <summary>Mark after commit / ship that still needs domain last_ship.</summary>
    public static void Mark(string? why = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir is { Length: > 0 })
                Directory.CreateDirectory(dir);
            var doc = new
            {
                schema = "domain_stamp_pending/v0",
                marked_utc = DateTimeOffset.UtcNow.ToString("o"),
                why = why ?? "git_commit"
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(doc));
        }
        catch
        {
            /* best-effort */
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch
        {
            /* best-effort */
        }
    }

    public static bool IsSet(TimeSpan? maxAge = null)
    {
        try
        {
            if (!File.Exists(FilePath))
                return false;
            var age = maxAge ?? TimeSpan.FromHours(48);
            var mtime = File.GetLastWriteTimeUtc(FilePath);
            return DateTime.UtcNow - mtime <= age;
        }
        catch
        {
            return false;
        }
    }

    public static string WhyLine() =>
        IsSet() ? "after commit — stamp last_ship same turn; L1 ≠ stamp moment" : "";
}
