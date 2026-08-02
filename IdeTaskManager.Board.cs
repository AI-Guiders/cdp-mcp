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

    /// <summary>Mirror Task Manager pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass(
        IntentWorkspaceStore? store,
        IntentWorkspaceState state,
        string? sessionPhase = null)
    {
        try
        {
            if (store is null)
            {
                CidePlanLatch.Publish(active: false, pulse: "no task store", feature: null, task: null);
                return;
            }

            var snap = store.TaskManagerSnapshot(state);
            var pulse = PulseLine(store, state, sessionPhase);
            // Dark Cockpit: silent when no active feature.
            var active = snap.ActiveFeatureTitle is { Length: > 0 };
            CidePlanLatch.Publish(active, pulse, snap.ActiveFeatureTitle, snap.ActiveStageTitle);
        }
        catch
        {
            /* best-effort */
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
        var localNow = IdeLocalClock.Now;
        var localLine = IdeLocalClock.PulseLine(localNow);
        var daypart = IdeLocalClock.DayPart(localNow);
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
                ? $"{localLine} · {f} › {t} · {phaseWire}{wall}{events}"
                : $"{localLine} · {f} › (pick task) · {phaseWire}"
            : $"{localLine} · no plan — feature <name> · {phaseWire}";

        var banner = snap.ActiveFeatureTitle is { Length: > 0 }
            ? $"| local:{Trim($"{localNow:MM-dd HH:mm} {daypart}", 18)} | plan:{Trim(snap.ActiveFeatureTitle, 18)} | task:{Trim(snap.ActiveStageTitle ?? "—", 18)} | phase:{phaseWire} |{WallBanner(wall + events)}"
            : $"| local:{Trim($"{localNow:MM-dd HH:mm} {daypart}", 18)} | plan:— | task:— | phase:{phaseWire} |";

        if (snap.ActiveStagePhaseAffinity is { Length: > 0 } aff
            && sessionPhase is { Length: > 0 }
            && !aff.Equals(sessionPhase, StringComparison.OrdinalIgnoreCase))
        {
            lines.Insert(0, $"·phase mismatch task@{aff} · session={sessionPhase}");
        }

        return new Board(
            Pulse: pulse,
            View: new
            {
                schema = SchemaVersion,
                banner,
                board = lines.ToArray(),
                ascii = string.Join('\n', lines),
                local = localLine,
                hint = "Scan board. * = active feature; [>] active task; [x] done; [-] parked; [~] deferred; [ ] pending; @phase = affinity. wall= stage Start→Completed; local= host machine clock (go=calendar)."
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
                clock_kind = "wall+local",
                local = IdeLocalClock.PulseCard(localNow),
                deadlines = IdeLocalClock.Deadlines(localNow)
            });
    }
}
