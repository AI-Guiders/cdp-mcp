#nullable enable

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static IEnumerable<string> FormatStageTree(
        IReadOnlyList<StageNode> stages,
        Guid? activeStageId,
        int indent)
    {
        var roots = stages.Where(s => s.ParentId is null).OrderBy(s => s.Ordinal).ToList();
        foreach (var root in roots)
        {
            foreach (var line in Walk(root, stages, activeStageId, indent))
                yield return line;
        }

        var ids = stages.Select(s => s.Id).ToHashSet();
        foreach (var orphan in stages.Where(s => s.ParentId is { } p && !ids.Contains(p)).OrderBy(s => s.Ordinal))
        {
            foreach (var line in Walk(orphan, stages, activeStageId, indent))
                yield return line;
        }
    }

    static IEnumerable<string> Walk(
        StageNode node,
        IReadOnlyList<StageNode> all,
        Guid? activeStageId,
        int indent)
    {
        var pad = new string(' ', indent * 2);
        var box = node.Status.Equals("done", StringComparison.OrdinalIgnoreCase) ? "[x]"
            : node.Status.Equals("parked", StringComparison.OrdinalIgnoreCase) ? "[-]"
            : activeStageId == node.Id || node.Status.Equals("active", StringComparison.OrdinalIgnoreCase) ? "[>]"
            : "[ ]";
        var wall = FormatWallClockSuffix(node.StartedUtc, node.CompletedUtc, DateTimeOffset.UtcNow);
        yield return $"{pad}|--- {box} {node.Title}{(node.PhaseAffinity is { Length: > 0 } pa ? $" @{pa}" : "")}{wall}";
        foreach (var child in all.Where(s => s.ParentId == node.Id).OrderBy(s => s.Ordinal))
        {
            foreach (var line in Walk(child, all, activeStageId, indent + 1))
                yield return line;
        }
    }

    /// <summary>Human wall span — calendar Start→Completed (or Start→now while open). Not agent-active.</summary>
    internal static string FormatWallElapsed(DateTimeOffset start, DateTimeOffset end)
    {
        var span = end - start;
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        if (span.TotalSeconds < 60)
            return $"{(int)span.TotalSeconds}s";
        if (span.TotalMinutes < 60)
            return span.Seconds == 0
                ? $"{(int)span.TotalMinutes}m"
                : $"{(int)span.TotalMinutes}m{span.Seconds:D2}s";
        return span.Minutes == 0
            ? $"{(int)span.TotalHours}h"
            : $"{(int)span.TotalHours}h{span.Minutes:D2}m";
    }

    static string FormatWallClockSuffix(DateTimeOffset? started, DateTimeOffset? completed, DateTimeOffset now)
    {
        if (started is null)
            return "";
        if (completed is { } done)
            return $" · wall {FormatWallElapsed(started.Value, done)}";
        return $" · wall …{FormatWallElapsed(started.Value, now)}";
    }

    /// <summary>wait/fail/note pointers — SA diagnostic composition of wall, not a score.</summary>
    internal static string FormatEventCountsSuffix(int wait, int fail, int note)
    {
        if (wait == 0 && fail == 0 && note == 0)
            return "";
        var parts = new List<string>();
        if (wait > 0) parts.Add($"wait×{wait}");
        if (fail > 0) parts.Add($"fail×{fail}");
        if (note > 0) parts.Add($"note×{note}");
        return " · " + string.Join(' ', parts);
    }

    static string WallBanner(string wallSuffix) =>
        wallSuffix.Length == 0 ? "" : $" {wallSuffix.TrimStart(' ', '·').Trim()} |";

    static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
