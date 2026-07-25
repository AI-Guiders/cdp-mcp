#nullable enable
using System.Text;
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Spike: promote Task Manager board → human-inbox markdown; confirm/reject without pasting plan into chat.
/// Agent chat line = <c>chat</c> only (path). Harness owns the MD render.
/// </summary>
internal static class IdePlanPromote
{
    public const string SchemaVersion = "plan_promote/v0";
    public const string Awaiting = "awaiting_confirm";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";

    public static object Promote(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        string? notes,
        string? dirOverride)
    {
        var snap = store.TaskManagerSnapshot(state);
        if (snap.ActiveFeatureTitle is not { Length: > 0 } feature)
            throw new ArgumentException("no active feature — feature <name> first, then promote");

        var board = IdeTaskManager.BuildBoard(store, state);
        var planId = Guid.NewGuid().ToString("N")[..12];
        var dir = ResolveInbox(projectRoot, dirOverride);
        Directory.CreateDirectory(dir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var slug = Slug(feature);
        var mdPath = Path.Combine(dir, $"plan-{stamp}-{slug}.md");
        var statusPath = Path.ChangeExtension(mdPath, ".json");
        var latestMd = Path.Combine(dir, "LATEST.md");
        var latestJson = Path.Combine(dir, "LATEST.json");

        var md = RenderMarkdown(planId, feature, snap, board, notes, projectRoot);
        File.WriteAllText(mdPath, md, Encoding.UTF8);

        var status = new PlanStatus(
            SchemaVersion,
            planId,
            Awaiting,
            mdPath,
            feature,
            snap.ActiveFeatureId,
            snap.ActiveStageId,
            snap.ActiveStageTitle,
            DateTime.UtcNow,
            null,
            notes);
        WriteStatus(statusPath, status);
        File.Copy(mdPath, latestMd, overwrite: true);
        WriteStatus(latestJson, status);

        return new
        {
            op = "promote",
            schema = SchemaVersion,
            plan_id = planId,
            status = Awaiting,
            path = mdPath,
            status_path = statusPath,
            latest = latestMd,
            inbox = dir,
            // Thin chat payload — agent relays this only.
            chat = $"План: {mdPath}",
            hint = "Human reads file; then cmd=confirm (or reject). Do not paste plan body into chat."
        };
    }

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

    public static string ResolveInbox(string? projectRoot, string? dirOverride)
    {
        if (!string.IsNullOrWhiteSpace(dirOverride))
            return Path.GetFullPath(dirOverride);
        if (!string.IsNullOrWhiteSpace(projectRoot))
            return Path.GetFullPath(Path.Combine(projectRoot, ".cdp", "plans"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "plans");
    }

    static string RenderMarkdown(
        string planId,
        string feature,
        IdeTaskManager.Snapshot snap,
        IdeTaskManager.Board board,
        string? notes,
        string? projectRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Plan: {feature}");
        sb.AppendLine();
        sb.AppendLine($"- plan_id: `{planId}`");
        sb.AppendLine($"- status: `{Awaiting}`");
        sb.AppendLine($"- promoted_utc: `{DateTime.UtcNow:O}`");
        if (projectRoot is { Length: > 0 })
            sb.AppendLine($"- project_root: `{projectRoot}`");
        if (snap.ActiveStageTitle is { Length: > 0 } task)
            sb.AppendLine($"- focus_task: `{task}`");
        sb.AppendLine();
        sb.AppendLine("## Board");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(board.View is null ? "(empty)" : GetAscii(board));
        sb.AppendLine("```");
        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine();
            sb.AppendLine(notes.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("## Confirm");
        sb.AppendLine();
        sb.AppendLine("In agent IDE cockpit: `cmd=confirm` or `cmd=reject`.");
        sb.AppendLine("Do not require the agent to paste this file into chat.");
        return sb.ToString();
    }

    static string GetAscii(IdeTaskManager.Board board)
    {
        try
        {
            var json = JsonSerializer.Serialize(board.View);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ascii", out var a) && a.GetString() is { Length: > 0 } s)
                return s;
        }
        catch
        {
            /* fall through */
        }

        return board.Pulse;
    }

    static void WriteStatus(string path, PlanStatus status)
    {
        var payload = new
        {
            schema = status.Schema,
            plan_id = status.PlanId,
            status = status.Status,
            path = status.Path,
            feature = status.Feature,
            feature_id = status.FeatureId,
            task_id = status.TaskId,
            task = status.Task,
            promoted_utc = status.PromotedUtc,
            resolved_utc = status.ResolvedUtc,
            notes = status.Notes
        };
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    static PlanStatus? ReadStatus(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            return new PlanStatus(
                r.TryGetProperty("schema", out var sch) ? sch.GetString() ?? SchemaVersion : SchemaVersion,
                r.GetProperty("plan_id").GetString() ?? "",
                r.GetProperty("status").GetString() ?? Awaiting,
                r.GetProperty("path").GetString() ?? "",
                r.TryGetProperty("feature", out var f) ? f.GetString() : null,
                r.TryGetProperty("feature_id", out var fid) && Guid.TryParse(fid.GetString(), out var fg) ? fg : null,
                r.TryGetProperty("task_id", out var tid) && Guid.TryParse(tid.GetString(), out var tg) ? tg : null,
                r.TryGetProperty("task", out var t) ? t.GetString() : null,
                r.TryGetProperty("promoted_utc", out var pu) && DateTime.TryParse(pu.GetString(), out var pdt)
                    ? pdt.ToUniversalTime()
                    : DateTime.UtcNow,
                r.TryGetProperty("resolved_utc", out var ru) && DateTime.TryParse(ru.GetString(), out var rdt)
                    ? rdt.ToUniversalTime()
                    : null,
                r.TryGetProperty("notes", out var n) ? n.GetString() : null);
        }
        catch
        {
            return null;
        }
    }

    static string Slug(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch is ' ' or '-' or '_' && sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "plan" : s.Length <= 32 ? s : s[..32];
    }

    public sealed record PlanStatus(
        string Schema,
        string PlanId,
        string Status,
        string Path,
        string? Feature,
        Guid? FeatureId,
        Guid? TaskId,
        string? Task,
        DateTime PromotedUtc,
        DateTime? ResolvedUtc,
        string? Notes);
}
