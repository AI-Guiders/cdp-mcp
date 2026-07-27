#nullable enable
using System.Text;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdePlanPromote
{
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
            chat = $"План: {mdPath}",
            hint = "Human reads file; then cmd=confirm (or reject). Do not paste plan body into chat."
        };
    }
}
