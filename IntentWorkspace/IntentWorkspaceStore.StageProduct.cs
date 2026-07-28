#nullable enable
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    /// <summary>Existing WitDB may predate Stage.Product — ALTER if missing.</summary>
    public void EnsureStageProductColumn()
    {
        WithDb(db =>
        {
            try
            {
                db.Database.ExecuteSqlRaw(
                    "ALTER TABLE stages ADD COLUMN Product TEXT NULL;");
            }
            catch
            {
                // column already exists
            }
        });
    }

    public (Guid stage_id, string? product) StageSetProduct(IntentWorkspaceState state, Guid stageId, string? product)
    {
        var intentId = RequireIntent(state);
        var normalized = NormalizeProduct(product);
        return WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            entity.Product = normalized;
            entity.UpdatedUtc = DateTimeOffset.UtcNow;
            db.SaveChanges();
            return (entity.Id, entity.Product);
        });
    }

    /// <summary>Normalize freeform product/category tags. Known: Cursor|CDP|CIDE. clear/- → null.</summary>
    internal static string? NormalizeProduct(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim().TrimStart('#');
        if (t.Length == 0
            || t.Equals("clear", StringComparison.OrdinalIgnoreCase)
            || t.Equals("none", StringComparison.OrdinalIgnoreCase)
            || t.Equals("off", StringComparison.OrdinalIgnoreCase)
            || t.Equals("-", StringComparison.Ordinal))
            return null;
        if (t.Equals("cdp", StringComparison.OrdinalIgnoreCase))
            return "CDP";
        if (t.Equals("cursor", StringComparison.OrdinalIgnoreCase))
            return "Cursor";
        if (t.Equals("cide", StringComparison.OrdinalIgnoreCase))
            return "CIDE";
        return t;
    }
}
