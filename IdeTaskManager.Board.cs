#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>Task Manager board view — partials: Tree (format), Models (DTOs).</summary>
internal static partial class IdeTaskManager
{
    public static string PulseLine(
        IntentWorkspaceStore? store,
        IntentWorkspaceState state,
        string? sessionPhase = null)
    {
        if (store is null)
            return "no task store";
        try
        {
            return BuildBoard(store, state, sessionPhase).Pulse;
        }
        catch
        {
            return "task manager error";
        }
    }

    public static Board BuildBoard(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? sessionPhase = null)
    {
        var snap = store.TaskManagerSnapshot(state);
        var lines = new List<string>();
        foreach (var feature in snap.Features)
        {
            var mark = feature.IsActive ? "*" : " ";
            lines.Add($"{mark}{feature.Title}");
            if (!feature.IsActive)
                continue;
            foreach (var line in FormatStageTree(feature.Stages, feature.ActiveStageId, indent: 0))
                lines.Add(line);
        }

        if (lines.Count == 0)
            lines.Add("(empty — cmd=\"feature <name>\")");

        var phaseWire = sessionPhase is { Length: > 0 } ? sessionPhase : "—";
        var wall = FormatWallClockSuffix(snap.ActiveStageStartedUtc, snap.ActiveStageCompletedUtc, DateTimeOffset.UtcNow);
        var events = "";
        if (snap.ActiveStageId is { } eventStageId && snap.ActiveStageStartedUtc is not null)
        {
            try
            {
                var c = store.StageEventCounts(eventStageId);
                events = FormatEventCountsSuffix(c.Wait, c.Fail, c.Note);
                var phaseRows = store.StageEventPhaseRows(eventStageId);
                var end = snap.ActiveStageCompletedUtc ?? DateTimeOffset.UtcNow;
                events = FormatPhaseSegmentsSuffix(phaseRows, end) + events;
            }
            catch { /* diagnostic only */ }
        }

        var pulse = snap.ActiveFeatureTitle is { Length: > 0 } f
            ? snap.ActiveStageTitle is { Length: > 0 } t
                ? $"{f} › {t} · {phaseWire}{wall}{events}"
                : $"{f} › (pick task) · {phaseWire}"
            : $"no plan — feature <name> · {phaseWire}";

        var banner = snap.ActiveFeatureTitle is { Length: > 0 }
            ? $"| plan:{Trim(snap.ActiveFeatureTitle, 18)} | task:{Trim(snap.ActiveStageTitle ?? "—", 18)} | phase:{phaseWire} |{WallBanner(wall + events)}"
            : $"| plan:— | task:— | phase:{phaseWire} |";

        if (snap.ActiveStagePhaseAffinity is { Length: > 0 } aff
            && sessionPhase is { Length: > 0 }
            && !aff.Equals(sessionPhase, StringComparison.OrdinalIgnoreCase))
        {
            lines.Insert(0, $"!phase mismatch task@{aff} · session={sessionPhase}");
        }

        return new Board(
            Pulse: pulse,
            View: new
            {
                schema = SchemaVersion,
                banner,
                board = lines.ToArray(),
                ascii = string.Join('\n', lines),
                hint = "Scan board. * = active feature; [>] active task; [x] done; @phase = affinity. wall= calendar Start→Completed (not agent-active score)."
            },
            Focus: new
            {
                feature_id = snap.ActiveFeatureId,
                feature = snap.ActiveFeatureTitle,
                task_id = snap.ActiveStageId,
                task = snap.ActiveStageTitle,
                task_phase = snap.ActiveStagePhaseAffinity,
                session_phase = sessionPhase,
                started_utc = snap.ActiveStageStartedUtc,
                completed_utc = snap.ActiveStageCompletedUtc,
                elapsed = wall.Length > 0 || events.Length > 0
                    ? (wall + events).TrimStart(' ', '·').Trim()
                    : null,
                clock_kind = "wall"
            });
    }
}
