#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>Task Manager board view — partials: Tree (format), Models (DTOs).</summary>
internal static partial class IdeTaskManager
{
    // Lived 2026-08-05: go=plan pulse ~20s — Handle + CollectWork + PublishGlass each called BuildBoard
    // (3× TaskManagerSnapshot / WithDb). Same CallTool: reuse one board until focus/phase mutates.
    [ThreadStatic] static Board? s_boardCache;
    [ThreadStatic] static Guid? s_cacheIntentId;
    [ThreadStatic] static Guid? s_cacheStageId;
    [ThreadStatic] static string? s_cachePhase;
    [ThreadStatic] static int s_boardCacheHits;

    /// <summary>Test seam: cache hits since last InvalidateBoardCache.</summary>
    internal static int BoardCacheHits => s_boardCacheHits;

    public static void InvalidateBoardCache()
    {
        s_boardCache = null;
        s_cacheIntentId = null;
        s_cacheStageId = null;
        s_cachePhase = null;
        s_boardCacheHits = 0;
    }

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
            var board = BuildBoard(store, state, sessionPhase);
            var pulse = board.Pulse;
            // Dark Cockpit: silent when no active feature.
            var active = snap.ActiveFeatureTitle is { Length: > 0 };
            // Shared-SSOT: WHY from sealed course on same latch as NEXT (feature/task).
            var why = IdePressureChannel.CompactWhyLine(IdePressureChannel.TryPeekSealedCourse());
            CidePlanLatch.Publish(active, pulse, snap.ActiveFeatureTitle, snap.ActiveStageTitle, why, board.Lines);
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
        var phaseKey = sessionPhase ?? "";
        if (s_boardCache is Board hit
            && s_cacheIntentId == state.ActiveIntentId
            && s_cacheStageId == state.ActiveStageId
            && string.Equals(s_cachePhase, phaseKey, StringComparison.Ordinal))
        {
            s_boardCacheHits++;
            return hit;
        }

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
                var openReviews = 0;
                try { openReviews = store.StageEventOpenReviewCount(eventStageId); } catch { /* diagnostic */ }
                events = FormatEventCountsSuffix(c.Wait, c.Fail, c.Note, openReviews);
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

        var waveActive = IdeWaveChannel.TryLoadActive();
        if (waveActive is not null)
        {
            var wDone = waveActive.Items.Count(i => i.Status == "done");
            lines.Insert(0,
                $"~wave {waveActive.Status} {wDone}/{waveActive.Items.Count} · {Trim(waveActive.Title, 32)} · cmd=wave scene");
            pulse = $"{IdeWaveChannel.PulseLine()} · {pulse}";
            banner = $"| wave:{Trim($"{waveActive.Status} {wDone}/{waveActive.Items.Count}", 18)} " + banner[1..];
        }

        var board = new Board(
            Pulse: pulse,
            View: new
            {
                schema = SchemaVersion,
                banner,
                board = lines.ToArray(),
                ascii = string.Join('\n', lines),
                local = localLine,
                wave = waveActive is null
                    ? null
                    : new
                    {
                        id = waveActive.Id,
                        title = waveActive.Title,
                        status = waveActive.Status,
                        done = waveActive.Items.Count(i => i.Status == "done"),
                        total = waveActive.Items.Count
                    },
                hint = "Scan board. * = active feature; [>] active task; [x] done; [-] parked; [~] deferred; [ ] pending; @phase = affinity. ~wave = active throughput wave (cmd=wave …). wall= stage Start→Completed; local= host machine clock (go=calendar)."
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
            },
            Lines: lines);
        s_boardCache = board;
        s_cacheIntentId = state.ActiveIntentId;
        s_cacheStageId = state.ActiveStageId;
        s_cachePhase = phaseKey;
        return board;
    }
}
