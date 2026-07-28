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

    public static object BuildCriteriaSummary(IReadOnlyList<StageCriterionEntity> rows)
    {
        static int CountKind(IReadOnlyList<StageCriterionEntity> list, string kind) =>
            list.Count(x => x.Kind == kind);

        static int MetKind(IReadOnlyList<StageCriterionEntity> list, string kind) =>
            list.Count(x => x.Kind == kind && x.Status is "met" or "waived");

        return new
        {
            total = rows.Count,
            met = rows.Count(x => x.Status is "met" or "waived"),
            pending = rows.Count(x => x.Status == "pending"),
            unmet = rows.Count(x => x.Status == "unmet"),
            dor = new { total = CountKind(rows, "dor"), met = MetKind(rows, "dor") },
            ac = new { total = CountKind(rows, "ac"), met = MetKind(rows, "ac") },
            dod = new { total = CountKind(rows, "dod"), met = MetKind(rows, "dod") }
        };
    }

    /// <summary>
    /// Ship-ready leftover: every AC and every DoD row is met/waived.
    /// Vacuous (zero AC or zero DoD) is not ready — DoR alone never qualifies.
    /// </summary>
    public static bool IsAcDodShipReady(IReadOnlyList<StageCriterionEntity> rows)
    {
        var acTotal = 0;
        var acMet = 0;
        var dodTotal = 0;
        var dodMet = 0;
        foreach (var row in rows)
        {
            if (row.Kind == "ac")
            {
                acTotal++;
                if (row.Status is "met" or "waived") acMet++;
            }
            else if (row.Kind == "dod")
            {
                dodTotal++;
                if (row.Status is "met" or "waived") dodMet++;
            }
        }

        return acTotal > 0 && dodTotal > 0 && acMet == acTotal && dodMet == dodTotal;
    }

    /// <summary>
    /// Parked/deferred stages whose AC+DoD are fully met (excludes active focus by default).
    /// </summary>
    public IReadOnlyList<LeftoverShipCandidate> StageListLeftoverShipReady(
        IntentWorkspaceState state,
        bool includeActiveFocus = false)
    {
        var intentId = RequireIntent(state);
        var active = state.ActiveStageId;
        return WithDb(db =>
        {
            var rows = db.Stages.AsNoTracking()
                .Where(x => x.IntentId == intentId
                            && (x.Status == "parked" || x.Status == "deferred"))
                .OrderBy(x => x.Ordinal)
                .ToList();
            if (!includeActiveFocus && active is { } a)
                rows = rows.Where(x => x.Id != a).ToList();

            var stageIds = rows.Select(x => x.Id).ToList();
            if (stageIds.Count == 0)
                return (IReadOnlyList<LeftoverShipCandidate>)Array.Empty<LeftoverShipCandidate>();

            var criteria = db.StageCriteria.AsNoTracking()
                .Where(x => stageIds.Contains(x.StageId))
                .ToList();
            var byStage = criteria.GroupBy(x => x.StageId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<StageCriterionEntity>)g.ToList());

            var list = new List<LeftoverShipCandidate>();
            foreach (var stage in rows)
            {
                byStage.TryGetValue(stage.Id, out var rowsForStage);
                rowsForStage ??= Array.Empty<StageCriterionEntity>();
                if (!IsAcDodShipReady(rowsForStage))
                    continue;
                list.Add(new LeftoverShipCandidate(
                    stage.Id,
                    stage.Title,
                    stage.Status,
                    BuildCriteriaSummary(rowsForStage)));
            }

            return (IReadOnlyList<LeftoverShipCandidate>)list;
        });
    }

    public readonly record struct LeftoverShipCandidate(
        Guid TaskId,
        string Title,
        string Status,
        object CriteriaSummary);

    static object CriterionDto(StageCriterionEntity e) => new
    {
        op = "criterion",
        criterion_id = e.Id,
        stage_id = e.StageId,
        kind = e.Kind,
        text = e.Body,
        mode = e.Mode,
        status = e.Status,
        evidence_ref = e.EvidenceRef,
        ordinal = e.Ordinal,
        updated_utc = e.UpdatedUtc
    };

    internal static string NormalizeCriterionKind(string? raw)
    {
        var k = (raw ?? "").Trim().ToLowerInvariant();
        k = k switch
        {
            "dor" or "definition_of_ready" or "definition-of-ready" or "ready" => "dor",
            "ac" or "acceptance" or "acceptance_criteria" or "acceptance-criteria" => "ac",
            "dod" or "definition_of_done" or "definition-of-done" or "done" => "dod",
            _ => k
        };
        if (!CriterionKinds.Contains(k))
            throw new ArgumentException("criterion kind must be dor|ac|dod");
        return k;
    }

    internal static string NormalizeCriterionMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "manual";
        var m = raw.Trim().ToLowerInvariant();
        if (!CriterionModes.Contains(m))
            throw new ArgumentException("criterion mode must be manual|auto|hybrid");
        return m;
    }

    internal static string NormalizeCriterionStatus(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (!CriterionStatuses.Contains(s))
            throw new ArgumentException("criterion status must be pending|met|unmet|waived");
        return s;
    }

    static string TruncateCriterionText(string? text)
    {
        var t = (text ?? "").Trim();
        return t.Length <= 400 ? t : t[..400];
    }

    static string? TruncateEvidenceRef(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        return t.Length <= 160 ? t : t[..160];
    }
}
