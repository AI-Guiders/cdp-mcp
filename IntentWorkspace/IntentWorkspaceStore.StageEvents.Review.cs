#nullable enable
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    public const string ReviewKind = "review";
    public const string ReviewAckKind = "review.ack";

    /// <summary>
    /// Operator review remark bound to open leaf — survives chat compact.
    /// Dialog stays dialog; agent stamps via <c>cmd=review</c>.
    /// </summary>
    public object StageEventReviewAdd(IntentWorkspaceState state, Guid stageId, string text, string? source = null)
    {
        var intentId = RequireIntent(state);
        var body = TruncateSummary(text, 280);
        if (body.Length == 0)
            throw new ArgumentException("review needs text — review <remark>");
        var src = string.IsNullOrWhiteSpace(source) ? "operator" : source.Trim();
        if (src.Length > 32)
            src = src[..32];

        return WithDb(db =>
        {
            var stage = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                        ?? throw new ArgumentException($"stage_id not found: {stageId}");
            if (stage.StartedUtc is null || stage.CompletedUtc is not null)
                throw new ArgumentException("review needs open clock — cmd=start first");
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            db.StageEvents.Add(new StageEventEntity
            {
                Id = id,
                StageId = stageId,
                Utc = now,
                Kind = ReviewKind,
                Source = src,
                Summary = body,
                Ref = "open"
            });
            if (db.SaveChanges() <= 0)
                throw new InvalidOperationException("stage_events review SaveChanges wrote 0 rows");
            db.ChangeTracker.Clear();
            if (!db.StageEvents.Any(e => e.Id == id))
                throw new InvalidOperationException("stage_events review not durable after save");
            return new
            {
                op = "review",
                action = "add",
                task_id = stageId,
                review_id = id,
                kind = ReviewKind,
                source = src,
                utc = now,
                summary = body,
                open = CountOpenReviews(db, stageId),
                hint = "Operator remark saved on leaf — dig review list before done; ack id= when addressed."
            };
        });
    }

    public object StageEventReviewList(IntentWorkspaceState state, Guid stageId, bool openOnly = true)
    {
        var intentId = RequireIntent(state);
        return WithDb(db =>
        {
            _ = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                ?? throw new ArgumentException($"stage_id not found: {stageId}");
            var rows = ListReviews(db, stageId, openOnly);
            return new
            {
                op = "review",
                action = "list",
                task_id = stageId,
                open_only = openOnly,
                open = CountOpenReviews(db, stageId),
                count = rows.Count,
                reviews = rows,
                hint = "Dig open reviews before done. ack <review_id> closes one."
            };
        });
    }

    public object StageEventReviewAck(IntentWorkspaceState state, Guid stageId, Guid reviewId, string? note = null)
    {
        var intentId = RequireIntent(state);
        var ackNote = TruncateSummary(note ?? "acked", 120);
        return WithDb(db =>
        {
            _ = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                ?? throw new ArgumentException($"stage_id not found: {stageId}");
            var review = StageEventsForStage(db, stageId)
                .FirstOrDefault(e => e.Id == reviewId && e.Kind == ReviewKind);
            if (review is null)
                throw new ArgumentException($"review not found: {reviewId}");
            if (IsReviewAcked(db, stageId, reviewId))
            {
                return (object)new
                {
                    op = "review",
                    action = "ack",
                    task_id = stageId,
                    review_id = reviewId,
                    already = true,
                    open = CountOpenReviews(db, stageId),
                    hint = "already acked"
                };
            }

            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            db.StageEvents.Add(new StageEventEntity
            {
                Id = id,
                StageId = stageId,
                Utc = now,
                Kind = ReviewAckKind,
                Source = "agent",
                Summary = ackNote,
                Ref = reviewId.ToString("N")
            });
            if (db.SaveChanges() <= 0)
                throw new InvalidOperationException("stage_events review.ack SaveChanges wrote 0 rows");
            db.ChangeTracker.Clear();
            return (object)new
            {
                op = "review",
                action = "ack",
                task_id = stageId,
                review_id = reviewId,
                ack_id = id,
                utc = now,
                open = CountOpenReviews(db, stageId),
                hint = "Review closed — dig remaining open before done."
            };
        });
    }

    public int StageEventOpenReviewCount(Guid stageId) =>
        WithDb(db => CountOpenReviews(db, stageId));

    public IReadOnlyList<string> StageEventOpenReviewSummaries(Guid stageId, int take = 5) =>
        WithDb(db =>
        {
            take = Math.Clamp(take, 1, 20);
            var acked = AckedReviewIds(db, stageId);
            return StageEventsForStage(db, stageId)
                .Where(e => e.Kind == ReviewKind && !acked.Contains(e.Id))
                .OrderBy(e => e.Utc)
                .Select(e => e.Summary)
                .Take(take)
                .ToList();
        });

    static int CountOpenReviews(IntentWorkspaceDbContext db, Guid stageId)
    {
        var acked = AckedReviewIds(db, stageId);
        return StageEventsForStage(db, stageId)
            .Where(e => e.Kind == ReviewKind)
            .Count(e => !acked.Contains(e.Id));
    }

    static bool IsReviewAcked(IntentWorkspaceDbContext db, Guid stageId, Guid reviewId)
    {
        var key = reviewId.ToString("N");
        return StageEventsForStage(db, stageId)
            .Any(e => e.Kind == ReviewAckKind && e.Ref == key);
    }

    static HashSet<Guid> AckedReviewIds(IntentWorkspaceDbContext db, Guid stageId)
    {
        var refs = StageEventsForStage(db, stageId)
            .Where(e => e.Kind == ReviewAckKind && e.Ref != null)
            .Select(e => e.Ref!)
            .ToList();
        var set = new HashSet<Guid>();
        foreach (var r in refs)
        {
            if (Guid.TryParseExact(r, "N", out var g) || Guid.TryParse(r, out g))
                set.Add(g);
        }

        return set;
    }

    static List<object> ListReviews(IntentWorkspaceDbContext db, Guid stageId, bool openOnly)
    {
        var acked = AckedReviewIds(db, stageId);
        var rows = StageEventsForStage(db, stageId)
            .Where(e => e.Kind == ReviewKind)
            .OrderBy(e => e.Utc)
            .ToList();
        var list = new List<object>();
        foreach (var e in rows)
        {
            var isOpen = !acked.Contains(e.Id);
            if (openOnly && !isOpen)
                continue;
            list.Add(new
            {
                review_id = e.Id,
                utc = e.Utc,
                source = e.Source,
                summary = e.Summary,
                open = isOpen
            });
        }

        return list;
    }
}
