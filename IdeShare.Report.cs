#nullable enable
using System.Text;
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeShare
{
    /// <summary>
    /// Status digest for operator — FYI, not promote/confirm.
    /// Writes <c>.cdp/share/</c>; agent gets thin <c>chat=</c> only.
    /// </summary>
    public static object ShareReport(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        string? notes,
        string? dirOverride,
        string? sessionPhase)
    {
        var snap = store.TaskManagerSnapshot(state);
        var board = IdeTaskManager.BuildBoard(store, state, sessionPhase);
        var feature = snap.ActiveFeatureTitle
                      ?? snap.Features.FirstOrDefault()?.Title
                      ?? "(no feature)";
        var bodyMd = RenderReportMarkdown(feature, snap, board, notes, projectRoot, sessionPhase);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var shareId = Guid.NewGuid().ToString("N")[..12];
        var fileName = $"report-{stamp}-{Slug(feature)}.md";
        var written = WriteOperatorShareFiles(
            projectRoot,
            dirOverride,
            fileName,
            bodyMd,
            p => new
            {
                schema = SchemaVersion,
                share_id = shareId,
                with = "operator",
                what = "report",
                ask = "none",
                status = "shared",
                path = p,
                feature,
                lines = CountLines(bodyMd),
                chars = bodyMd.Length,
                shared_utc = DateTime.UtcNow
            });
        var path = written.Path;
        var latest = written.LatestMd;
        var latestJson = written.LatestJson;
        var dir = written.Inbox;

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "share",
            with = "operator",
            what = "report",
            ask = "none",
            status = "shared",
            share_id = shareId,
            path,
            latest,
            latest_json = latestJson,
            inbox = dir,
            feature,
            lines = CountLines(bodyMd),
            chars = bodyMd.Length,
            chat = $"Shared report: {path}",
            next = new object[]
            {
                new { go = "plan", label = "Task Manager", why = "op=board" },
                new { go = "share", label = "Share again", why = "what=report" }
            },
            hint =
                "share with=operator what=report|digest — FYI status digest (not promote). " +
                "Relay chat= only; do not paste report body into agent chat."
        };
    }

    static string RenderReportMarkdown(
        string feature,
        IdeTaskManager.Snapshot snap,
        IdeTaskManager.Board board,
        string? notes,
        string? projectRoot,
        string? sessionPhase)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Status · {feature}");
        sb.AppendLine();
        sb.AppendLine("## Meta");
        sb.AppendLine();
        sb.AppendLine("| Name | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Shared | `{DateTime.UtcNow:O}` |");
        sb.AppendLine($"| Pulse | `{board.Pulse}` |");
        if (sessionPhase is { Length: > 0 })
            sb.AppendLine($"| Session phase | `{sessionPhase}` |");
        if (projectRoot is { Length: > 0 })
            sb.AppendLine($"| Project | `{projectRoot}` |");
        sb.AppendLine();

        sb.AppendLine("## Board");
        sb.AppendLine();
        sb.AppendLine("```");
        try
        {
            var viewJson = JsonSerializer.Serialize(board.View);
            using var doc = JsonDocument.Parse(viewJson);
            if (doc.RootElement.TryGetProperty("ascii", out var ascii)
                && ascii.GetString() is { Length: > 0 } a)
                sb.AppendLine(a);
            else if (doc.RootElement.TryGetProperty("board", out var lines)
                     && lines.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.EnumerateArray())
                    sb.AppendLine(line.GetString() ?? "");
            }
            else
                sb.AppendLine(board.Pulse);
        }
        catch
        {
            sb.AppendLine(board.Pulse);
        }

        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Todos");
        sb.AppendLine();
        var todos = IdePlanPromote.FormatTodos(snap);
        sb.AppendLine(todos.Length == 0 ? "- (empty)" : todos);

        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.AppendLine();
            sb.AppendLine("## Agent notes");
            sb.AppendLine();
            sb.AppendLine(notes.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("FYI digest — not a promote. No confirm required.");
        return sb.ToString();
    }
}
