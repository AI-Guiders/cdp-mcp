#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Durable key→value pins for dialog citizen (survives remount).
/// Injected as <c>sticky | k=v · …</c> on dialog turns — short facts beyond rolling chat.
/// </summary>
internal static class CitizenStickyFacts
{
    public const string FileName = "citizen-sticky.json";

    static readonly object Gate = new();
    static string? PathOverrideForTests;
    static Dictionary<string, string>? MemoryOverrideForTests;

    public static string FilePath =>
        PathOverrideForTests
        ?? Path.Combine(CdpProfile.StateRoot, IdeIgniteArmHost.Seat, FileName);

    internal static void SetTestPath(string? path) => PathOverrideForTests = path;

    internal static void SetTestMemory(Dictionary<string, string>? map)
    {
        lock (Gate)
            MemoryOverrideForTests = map;
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            PathOverrideForTests = null;
            MemoryOverrideForTests = null;
        }
    }

    public static IReadOnlyDictionary<string, string> Load()
    {
        lock (Gate)
        {
            if (MemoryOverrideForTests is not null)
                return new Dictionary<string, string>(MemoryOverrideForTests, StringComparer.OrdinalIgnoreCase);
        }

        var path = FilePath;
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(path);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                      ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return;
        var k = key.Trim();
        var v = value.Trim();
        lock (Gate)
        {
            if (MemoryOverrideForTests is not null)
            {
                MemoryOverrideForTests[k] = v;
                return;
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var map = new Dictionary<string, string>(Load(), StringComparer.OrdinalIgnoreCase) { [k] = v };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best-effort
        }
    }

    public static void Clear(string? key = null)
    {
        lock (Gate)
        {
            if (MemoryOverrideForTests is not null)
            {
                if (string.IsNullOrWhiteSpace(key))
                    MemoryOverrideForTests.Clear();
                else
                    MemoryOverrideForTests.Remove(key.Trim());
                return;
            }
        }

        try
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                return;
            }

            var map = new Dictionary<string, string>(Load(), StringComparer.OrdinalIgnoreCase);
            map.Remove(key.Trim());
            if (map.Count == 0)
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Wire-ish line for dialog afferent, or null when empty.</summary>
    public static string? AfferentLine()
    {
        var map = Load();
        if (map.Count == 0)
            return null;
        var parts = map
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={Trunc(kv.Value, 80)}");
        return "sticky | " + string.Join(" · ", parts);
    }

    public static object Pulse()
    {
        var map = Load();
        return new
        {
            path = FilePath,
            count = map.Count,
            keys = map.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
