#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdePlanPromote
{
    public static object Confirm(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        string? dirOverride,
        string? planId,
        bool reject)
    {
        _ = store;
        _ = state;
        var dir = ResolveInbox(projectRoot, dirOverride);
        var latestJson = Path.Combine(dir, "LATEST.json");
        if (!File.Exists(latestJson))
            throw new ArgumentException($"no promoted plan in {dir} — promote first");

        var status = ReadStatus(latestJson)
                     ?? throw new ArgumentException("LATEST.json unreadable");
        if (planId is { Length: > 0 }
            && !string.Equals(status.PlanId, planId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"plan_id mismatch: latest={status.PlanId}, asked={planId}");

        var next = reject ? Rejected : Confirmed;
        if (status.Status is Confirmed or Rejected
            && string.Equals(status.Status, next, StringComparison.Ordinal))
        {
            return new
            {
                op = reject ? "reject" : "confirm",
                schema = SchemaVersion,
                plan_id = status.PlanId,
                status = status.Status,
                path = status.Path,
                chat = reject ? $"План отклонён: {status.Path}" : $"План подтверждён: {status.Path}",
                idempotent = true
            };
        }

        var updated = status with
        {
            Status = next,
            ResolvedUtc = DateTime.UtcNow
        };
        WriteStatus(latestJson, updated);
        var sibling = Path.ChangeExtension(status.Path, ".json");
        if (File.Exists(sibling))
            WriteStatus(sibling, updated);

        return new
        {
            op = reject ? "reject" : "confirm",
            schema = SchemaVersion,
            plan_id = updated.PlanId,
            status = updated.Status,
            path = updated.Path,
            chat = reject
                ? $"План отклонён: {updated.Path}"
                : $"План подтверждён: {updated.Path}",
            hint = reject
                ? "Revise Task Manager board, then promote again."
                : "Continue execution — plan confirmed in IDE."
        };
    }

    public static object? TryPulse(string? projectRoot, string? dirOverride)
    {
        try
        {
            var dir = ResolveInbox(projectRoot, dirOverride);
            var latestJson = Path.Combine(dir, "LATEST.json");
            if (!File.Exists(latestJson))
                return null;
            var status = ReadStatus(latestJson);
            if (status is null)
                return null;
            return new
            {
                plan_id = status.PlanId,
                status = status.Status,
                path = status.Path,
                feature = status.Feature,
                chat = status.Status == Awaiting
                    ? $"План ждёт confirm: {status.Path}"
                    : null
            };
        }
        catch
        {
            return null;
        }
    }
}
