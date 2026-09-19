#nullable enable
using System.Collections.Concurrent;

namespace CdpMcp;

/// <summary>
/// In-proc thrash latch — per-path set_text_large ring + desk hot pulse (L3 Just Culture).
/// Reason language = habitat gap / repeat-fail — never blame.
/// </summary>
internal static class IdeThrashLatch
{
    static readonly ConcurrentDictionary<string, int> LargeSetTextByPath = new(StringComparer.OrdinalIgnoreCase);
    static string? _lastPath;
    static DateTimeOffset _lastUtc;

    public static void NoteSetTextLarge(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var key = path.Trim();
        LargeSetTextByPath.AddOrUpdate(key, 1, (_, n) => n + 1);
        _lastPath = key;
        _lastUtc = DateTimeOffset.UtcNow;
    }

    public static int LargeSetTextCount(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;
        return LargeSetTextByPath.TryGetValue(path.Trim(), out var n) ? n : 0;
    }

    public static bool IsHot(TimeSpan? window = null)
    {
        var w = window ?? TimeSpan.FromMinutes(15);
        return _lastPath is not null && DateTimeOffset.UtcNow - _lastUtc <= w;
    }

    public static string? PulseLine()
    {
        if (!IsHot() || _lastPath is null)
            return null;
        var n = LargeSetTextCount(_lastPath);
        return $"thrash · set_text_large×{n} · {Path.GetFileName(_lastPath)}";
    }

    public static void ResetForTests()
    {
        LargeSetTextByPath.Clear();
        _lastPath = null;
        _lastUtc = default;
    }
}
