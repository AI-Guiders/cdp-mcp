#nullable enable
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    /// <summary>Existing WitDB may predate Stage.Executor — ALTER if missing.</summary>
    public void EnsureStageExecutorColumn()
    {
        WithDb(db =>
        {
            try
            {
                db.Database.ExecuteSqlRaw(
                    "ALTER TABLE stages ADD COLUMN Executor TEXT NULL;");
            }
            catch
            {
                // column already exists
            }
        });
    }

    public (Guid stage_id, string? executor) StageSetExecutor(IntentWorkspaceState state, Guid stageId, string? executor)
    {
        var intentId = RequireIntent(state);
        var normalized = NormalizeExecutor(executor);
        return WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            entity.Executor = normalized;
            entity.UpdatedUtc = DateTimeOffset.UtcNow;
            db.SaveChanges();
            return (entity.Id, entity.Executor);
        });
    }

    /// <summary>Normalize Who executor. Known: Sierra|Кир|Света. clear/- → null. Strips leading ~|@.</summary>
    internal static string? NormalizeExecutor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim().TrimStart('~', '@');
        if (t.Length == 0
            || t.Equals("clear", StringComparison.OrdinalIgnoreCase)
            || t.Equals("none", StringComparison.OrdinalIgnoreCase)
            || t.Equals("off", StringComparison.OrdinalIgnoreCase)
            || t.Equals("-", StringComparison.Ordinal))
            return null;
        if (t.Equals("sierra", StringComparison.OrdinalIgnoreCase))
            return "Sierra";
        if (t.Equals("kir", StringComparison.OrdinalIgnoreCase)
            || t.Equals("кир", StringComparison.OrdinalIgnoreCase))
            return "Кир";
        if (t.Equals("sveta", StringComparison.OrdinalIgnoreCase)
            || t.Equals("света", StringComparison.OrdinalIgnoreCase))
            return "Света";
        return t;
    }
}
