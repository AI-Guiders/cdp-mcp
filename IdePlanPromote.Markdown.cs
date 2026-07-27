#nullable enable
using System.Text;

namespace CdpMcp;

internal static partial class IdePlanPromote
{
    static string RenderMarkdown(
        string planId,
        string feature,
        IdeTaskManager.Snapshot snap,
        IdeTaskManager.Board board,
        string? notes,
        string? projectRoot)
    {
        _ = board; // pulse board stays in cockpit; inbox prefers short todos
        var sb = new StringBuilder();
        sb.AppendLine($"# {feature}");
        sb.AppendLine();
        sb.AppendLine("## Plan Meta");
        sb.AppendLine();
        sb.AppendLine("| Name | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Id | `{planId}` |");
        sb.AppendLine($"| Status | `{Awaiting}` |");
        sb.AppendLine($"| Promoted | `{DateTime.UtcNow:O}` |");
        if (projectRoot is { Length: > 0 })
            sb.AppendLine($"| Project | `{projectRoot}` |");
        sb.AppendLine();
        sb.AppendLine("## Todos");
        sb.AppendLine();
        var todos = FormatTodos(snap);
        sb.AppendLine(todos.Length == 0 ? "- (empty — add tasks first)" : todos);
        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine();
            sb.AppendLine(notes.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("`cmd=approved` | `cmd=go around` | `cmd=stabilized` | `cmd=hold` — CRM panel (not chat reject essays).");
        return sb.ToString();
    }

    /// <summary>Active feature → markdown todo lines from stage statuses.</summary>
    internal static string FormatTodos(IdeTaskManager.Snapshot snap)
    {
        var feature = snap.Features.FirstOrDefault(f => f.IsActive)
                      ?? snap.Features.FirstOrDefault();
        if (feature is null || feature.Stages.Count == 0)
            return "";

        var stages = feature.Stages;
        var ids = stages.Select(s => s.Id).ToHashSet();
        var sb = new StringBuilder();
        foreach (var line in WalkTodos(stages, feature.ActiveStageId, parentId: null, indent: 0))
            sb.AppendLine(line);
        // Orphans (parent missing) — still list
        foreach (var orphan in stages.Where(s => s.ParentId is { } p && !ids.Contains(p)).OrderBy(s => s.Ordinal))
        {
            sb.AppendLine($"- [{TodoMark(orphan, feature.ActiveStageId)}] {orphan.Title}");
            foreach (var line in WalkTodos(stages, feature.ActiveStageId, orphan.Id, indent: 1))
                sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }

    static IEnumerable<string> WalkTodos(
        IReadOnlyList<IdeTaskManager.StageNode> stages,
        Guid? activeStageId,
        Guid? parentId,
        int indent)
    {
        foreach (var node in stages.Where(s => s.ParentId == parentId).OrderBy(s => s.Ordinal))
        {
            var pad = new string(' ', indent * 2);
            var mark = TodoMark(node, activeStageId);
            yield return $"{pad}- [{mark}] {node.Title}";
            foreach (var child in WalkTodos(stages, activeStageId, node.Id, indent + 1))
                yield return child;
        }
    }

    static char TodoMark(IdeTaskManager.StageNode node, Guid? activeStageId)
    {
        if (node.Status.Equals("done", StringComparison.OrdinalIgnoreCase))
            return 'x';
        if (node.Status.Equals("parked", StringComparison.OrdinalIgnoreCase))
            return '-';
        if (activeStageId == node.Id
            || node.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            return '>';
        return ' ';
    }

}
