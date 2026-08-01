#nullable enable
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    static readonly HashSet<string> CriterionKinds = new(StringComparer.Ordinal)
        { "dor", "ac", "dod" };
    static readonly HashSet<string> CriterionModes = new(StringComparer.Ordinal)
        { "manual", "auto", "hybrid" };
    static readonly HashSet<string> CriterionStatuses = new(StringComparer.Ordinal)
        { "pending", "met", "unmet", "waived" };

    public void EnsureStageCriteriaTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS stage_criteria (
                    Id GUID NOT NULL PRIMARY KEY,
                    StageId GUID NOT NULL,
                    Kind TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    Mode TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    EvidenceRef TEXT NULL,
                    Ordinal INTEGER NOT NULL,
                    UpdatedUtc DATETIMEOFFSET NOT NULL
                );
                """);
            try
            {
                db.Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS IX_stage_criteria_StageId_Ordinal ON stage_criteria (StageId, Ordinal);");
                db.Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS IX_stage_criteria_StageId_Kind ON stage_criteria (StageId, Kind);");
            }
            catch
            {
                // index already exists / engine variance
            }
        });
    }

    public object StageCriterionEnsure(
        IntentWorkspaceState state,
        Guid stageId,
        string kind,
        string text,
        string? mode = null,
        string? evidenceRef = null)
    {
        var intentId = RequireIntent(state);
        var k = NormalizeCriterionKind(kind);
        var body = TruncateCriterionText(text);
        if (body.Length == 0)
            throw new ArgumentException("criterion needs text — criterion dor|ac|dod <text>");
        var m = NormalizeCriterionMode(mode);
        var now = DateTimeOffset.UtcNow;
        var evidence = TruncateEvidenceRef(evidenceRef);
        return WithDb(db =>
        {
            _ = db.Stages.AsNoTracking().FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                ?? throw new ArgumentException($"stage_id not found: {stageId}");
            var existing = evidence is null
                ? db.StageCriteria.FirstOrDefault(x =>
                    x.StageId == stageId && x.Kind == k && x.Body == body)
                : db.StageCriteria.FirstOrDefault(x =>
                    x.StageId == stageId
                    && x.Kind == k
                    && (x.Body == body || x.EvidenceRef == evidence));
            if (existing is not null)
            {
                existing.Mode = m;
                if (evidence is not null)
                    existing.EvidenceRef = evidence;
                existing.UpdatedUtc = now;
                db.SaveChanges();
                return CriterionDto(existing);
            }

            var ordinal = db.StageCriteria.Count(x => x.StageId == stageId);
            var row = new StageCriterionEntity
            {
                Id = Guid.NewGuid(),
                StageId = stageId,
                Kind = k,
                Body = body,
                Mode = m,
                Status = "pending",
                EvidenceRef = evidence,
                Ordinal = ordinal,
                UpdatedUtc = now
            };
            db.StageCriteria.Add(row);
            if (db.SaveChanges() <= 0)
                throw new InvalidOperationException("stage_criteria ensure SaveChanges wrote 0 rows");
            return CriterionDto(row);
        });
    }

    public object StageCriterionAdd(
        IntentWorkspaceState state,
        Guid stageId,
        string kind,
        string text,
        string? mode = null,
        string? evidenceRef = null)
    {
        var intentId = RequireIntent(state);
        var k = NormalizeCriterionKind(kind);
        var body = TruncateCriterionText(text);
        if (body.Length == 0)
            throw new ArgumentException("criterion needs text — criterion dor|ac|dod <text>");
        var m = NormalizeCriterionMode(mode);
        var now = DateTimeOffset.UtcNow;
        return WithDb(db =>
        {
            _ = db.Stages.AsNoTracking().FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                ?? throw new ArgumentException($"stage_id not found: {stageId}");
            var ordinal = db.StageCriteria.Count(x => x.StageId == stageId);
            var row = new StageCriterionEntity
            {
                Id = Guid.NewGuid(),
                StageId = stageId,
                Kind = k,
                Body = body,
                Mode = m,
                Status = "pending",
                EvidenceRef = TruncateEvidenceRef(evidenceRef),
                Ordinal = ordinal,
                UpdatedUtc = now
            };
            db.StageCriteria.Add(row);
            if (db.SaveChanges() <= 0)
                throw new InvalidOperationException("stage_criteria add SaveChanges wrote 0 rows");
            return CriterionDto(row);
        });
    }

    public object StageCriterionSetStatus(
        IntentWorkspaceState state,
        Guid criterionId,
        string status,
        string? evidenceRef = null)
    {
        var intentId = RequireIntent(state);
        var st = NormalizeCriterionStatus(status);
        var now = DateTimeOffset.UtcNow;
        return WithDb(db =>
        {
            var row = db.StageCriteria.FirstOrDefault(x => x.Id == criterionId)
                      ?? throw new ArgumentException($"criterion not found: {criterionId}");
            _ = db.Stages.AsNoTracking().FirstOrDefault(x => x.Id == row.StageId && x.IntentId == intentId)
                ?? throw new ArgumentException($"criterion stage not in active feature: {criterionId}");
            row.Status = st;
            if (evidenceRef is not null)
                row.EvidenceRef = TruncateEvidenceRef(evidenceRef);
            row.UpdatedUtc = now;
            db.SaveChanges();
            return CriterionDto(row);
        });
    }

    public object StageCriterionDrop(IntentWorkspaceState state, Guid criterionId)
    {
        var intentId = RequireIntent(state);
        return WithDb(db =>
        {
            var row = db.StageCriteria.FirstOrDefault(x => x.Id == criterionId)
                      ?? throw new ArgumentException($"criterion not found: {criterionId}");
            _ = db.Stages.AsNoTracking().FirstOrDefault(x => x.Id == row.StageId && x.IntentId == intentId)
                ?? throw new ArgumentException($"criterion stage not in active feature: {criterionId}");
            var stageId = row.StageId;
            var kind = row.Kind;
            db.StageCriteria.Remove(row);
            db.SaveChanges();
            return new { op = "criterion_drop", criterion_id = criterionId, stage_id = stageId, kind };
        });
    }

    public object StageCriterionList(IntentWorkspaceState state, Guid stageId, string? kind = null)
    {
        var intentId = RequireIntent(state);
        string? kindFilter = kind is null ? null : NormalizeCriterionKind(kind);
        return WithDb(db =>
        {
            _ = db.Stages.AsNoTracking().FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                ?? throw new ArgumentException($"stage_id not found: {stageId}");
            var q = db.StageCriteria.AsNoTracking().Where(x => x.StageId == stageId);
            if (kindFilter is not null)
                q = q.Where(x => x.Kind == kindFilter);
            var rows = q.OrderBy(x => x.Ordinal).ThenBy(x => x.UpdatedUtc).ToList();
            return new
            {
                stage_id = stageId,
                count = rows.Count,
                summary = BuildCriteriaSummary(rows),
                criteria = rows.Select(CriterionDto).ToList()
            };
        });
    }
}
