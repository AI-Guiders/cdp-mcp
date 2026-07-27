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
        yield return $"{pad}|--- {box} {node.Title}{(node.PhaseAffinity is { Length: > 0 } pa ? $" @{pa}" : "")}";
        foreach (var child in all.Where(s => s.ParentId == node.Id).OrderBy(s => s.Ordinal))
        {
            foreach (var line in Walk(child, all, activeStageId, indent + 1))
                yield return line;
        }
    }

    static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
