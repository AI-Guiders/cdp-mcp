#nullable enable
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    static bool IsIncompleteStage(StageEntity s) =>
        s.Status is "pending" or "active"
        && !string.Equals(s.PhaseAffinity, "handoff", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// First incomplete leaf under the active intent (DFS by Ordinal).
    /// Leaf = incomplete stage with no incomplete children.
    /// </summary>
    public Guid? FindFirstIncompleteLeaf(IntentWorkspaceState state, Guid? underParentId = null)
    {
        if (state.ActiveIntentId is not { } intentId)
            return null;

        return WithDb(db =>
        {
            var stages = db.Stages.AsNoTracking()
                .Where(x => x.IntentId == intentId)
                .ToList();
            return WalkFirstIncompleteLeaf(stages, underParentId);
        });
    }

    /// <summary>
    /// Next incomplete leaf after <paramref name="afterStageId"/> in DFS tree order
    /// (afterId may already be done — still anchors position in the walk).
    /// </summary>
    public Guid? FindNextIncompleteLeaf(IntentWorkspaceState state, Guid? afterStageId)
    {
        if (state.ActiveIntentId is not { } intentId)
            return null;

        return WithDb(db =>
        {
            var stages = db.Stages.AsNoTracking()
                .Where(x => x.IntentId == intentId)
                .ToList();
            var ordered = EnumerateDfs(stages, underParentId: null).ToList();
            var start = afterStageId is null
                ? -1
                : ordered.FindIndex(x => x.Id == afterStageId.Value);
            for (var i = start + 1; i < ordered.Count; i++)
            {
                var n = ordered[i];
                if (IsIncompleteStage(n) && !HasIncompleteChild(stages, n.Id))
                    return (Guid?)n.Id;
            }

            return (Guid?)null;
        });
    }


    /// <summary>
    /// If <paramref name="stageId"/> is a container, resolve to first incomplete leaf under it;
    /// if already an incomplete leaf, return it; otherwise null.
    /// </summary>
    public Guid? ResolveIncompleteLeaf(IntentWorkspaceState state, Guid stageId)
    {
        if (state.ActiveIntentId is not { } intentId)
            return null;

        return WithDb(db =>
        {
            var stages = db.Stages.AsNoTracking()
                .Where(x => x.IntentId == intentId)
                .ToList();
            var self = stages.FirstOrDefault(x => x.Id == stageId);
            if (self is null)
                return null;

            var under = WalkFirstIncompleteLeaf(stages, stageId);
            if (under is not null)
                return under;

            return IsIncompleteStage(self) && !HasIncompleteChild(stages, stageId)
                ? stageId
                : null;
        });
    }

    static IEnumerable<StageEntity> EnumerateDfs(List<StageEntity> stages, Guid? underParentId)
    {
        foreach (var node in ChildrenOf(stages, underParentId))
        {
            yield return node;
            foreach (var d in EnumerateDfs(stages, node.Id))
                yield return d;
        }
    }

    static Guid? WalkFirstIncompleteLeaf(List<StageEntity> stages, Guid? underParentId)
    {
        foreach (var id in EnumerateIncompleteLeaves(stages, underParentId))
            return id;
        return null;
    }

    static IEnumerable<Guid> EnumerateIncompleteLeaves(List<StageEntity> stages, Guid? underParentId)
    {
        foreach (var node in ChildrenOf(stages, underParentId))
        {
            if (!IsIncompleteStage(node) && !HasIncompleteDescendant(stages, node.Id))
                continue;

            if (HasIncompleteChild(stages, node.Id))
            {
                foreach (var leaf in EnumerateIncompleteLeaves(stages, node.Id))
                    yield return leaf;
            }
            else if (IsIncompleteStage(node))
            {
                yield return node.Id;
            }
        }
    }

    static IEnumerable<StageEntity> ChildrenOf(List<StageEntity> stages, Guid? parentId) =>
        stages
            .Where(x => Nullable.Equals(x.ParentId, parentId))
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase);

    static bool HasIncompleteChild(List<StageEntity> stages, Guid parentId) =>
        stages.Any(x => x.ParentId == parentId && IsIncompleteStage(x));

    static bool HasIncompleteDescendant(List<StageEntity> stages, Guid parentId)
    {
        foreach (var child in stages.Where(x => x.ParentId == parentId))
        {
            if (IsIncompleteStage(child))
                return true;
            if (HasIncompleteDescendant(stages, child.Id))
                return true;
        }

        return false;
    }

    public string? StageTitle(IntentWorkspaceState state, Guid stageId) =>
        WithDb(db => db.Stages.AsNoTracking().FirstOrDefault(x => x.Id == stageId)?.Title);

    /// <summary>Mark every incomplete stage under the active intent as done. Returns count.</summary>
    public int MarkIncompleteStagesDone(IntentWorkspaceState state)
    {
        if (state.ActiveIntentId is not { } intentId)
            return 0;

        return WithDb(db =>
        {
            var rows = db.Stages
                .Where(x => x.IntentId == intentId && (x.Status == "pending" || x.Status == "active"))
                .ToList();
            var now = DateTimeOffset.UtcNow;
            foreach (var row in rows)
            {
                row.Status = "done";
                row.UpdatedUtc = now;
            }

            if (rows.Count > 0)
                db.SaveChanges();
            return rows.Count;
        });
    }

    public bool StageHasWallStart(IntentWorkspaceState state, Guid stageId)
    {
        if (state.ActiveIntentId is not { } intentId)
            return false;
        return WithDb(db =>
            db.Stages.AsNoTracking()
                .Any(x => x.Id == stageId && x.IntentId == intentId && x.StartedUtc != null));
    }
}
