#nullable enable
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

/// <summary>Stage criteria normalize/DTO/ship-ready (≤ADX soft-warn peel).</summary>
internal sealed partial class IntentWorkspaceStore
{
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
