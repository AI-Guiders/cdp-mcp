#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskDone(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var title = Title(args);
        var id = ResolveStageTarget(store, state, args);
        if (id is null)
            throw new ArgumentException(title.Length > 0
                ? $"task not found: {title}"
                : "done needs active task or title — focus X | done X");

        var r = store.StageSetStatus(state, id.Value, "done");
        if (state.ActiveStageId == id)
        {
            var next = store.FindNextPendingStage(state);
            if (next is { } n)
                store.FocusStage(state, n);
            else
            {
                state.ActiveStageId = null;
                store.WorkFocusSave(state);
            }
        }

        return new { op = "done", task_id = r.stage_id, status = r.status };
    }

    static object TaskStatus(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args,
        string status)
    {
        var title = Title(args);
        var id = ResolveStageTarget(store, state, args);
        if (id is null)
            throw new ArgumentException(title.Length > 0
                ? $"task not found: {title}"
                : $"{status} needs task id, title, or active focus");

        if (status == "active")
        {
            store.FocusStage(state, id.Value);
            return new { op = "active", task_id = id };
        }

        object? clock = null;
        if (status == "parked")
        {
            IdeStageCycle.TryPhaseComplete(); // close open phase visit before freeze
            clock = store.StageClockParkFreeze(state, id.Value);
        }

        var wasActive = state.ActiveStageId == id;
        var r = store.StageSetStatus(state, id.Value, status);
        if (wasActive && status is "parked" or "deferred")
        {
            var next = store.FindNextPendingStage(state);
            if (next is { } n)
                store.FocusStage(state, n);
            else
            {
                state.ActiveStageId = null;
                store.WorkFocusSave(state);
            }
        }
        else
            store.WorkFocusSave(state);

        return clock is null
            ? new { op = status, task_id = r.stage_id, status = r.status }
            : new { op = status, task_id = r.stage_id, status = r.status, clock };
    }

    static object TaskClockStart(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveClockStageId(store, state, args)
                 ?? throw new ArgumentException("start needs active task or title — focus X | start X");
        var r = store.StageClockStart(state, id);
        IdeStageCycle.TryPhaseStart(); // open wall segment for current session phase
        return r;
    }

    static object TaskClockShipped(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveClockStageId(store, state, args)
                 ?? throw new ArgumentException("shipped needs active task or title — start first, then shipped");
        IdeStageCycle.TryPhaseComplete(); // close open phase segment before clock end
        return store.StageClockShipped(state, id);
    }

    static object TaskPhaseStart(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveClockStageId(store, state, args)
                 ?? throw new ArgumentException("start_phase needs open clock — focus + start first");
        if (state.ActiveStageId != id)
            store.FocusStage(state, id);
        var phase = PhaseArg(args);
        if (string.IsNullOrWhiteSpace(phase))
        {
            var title = Title(args);
            phase = title.Length > 0 ? title : null;
        }

        if (!IdeStageCycle.TryPhaseStart(phase, out var used))
            throw new ArgumentException("start_phase needs open clock + phase — start first; start_phase act | session phase via cdp_context");
        return new
        {
            op = "start_phase",
            task_id = id,
            phase = used,
            hint = "wall phase segment begin — SA diagnostic, not a score"
        };
    }

    static object TaskPhaseComplete(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveClockStageId(store, state, args)
                 ?? throw new ArgumentException("complete_phase needs open clock — focus + start first");
        if (state.ActiveStageId != id)
            store.FocusStage(state, id);
        var phase = PhaseArg(args);
        if (string.IsNullOrWhiteSpace(phase))
        {
            var title = Title(args);
            phase = title.Length > 0 ? title : null;
        }

        if (!IdeStageCycle.TryPhaseComplete(phase, out var used))
            throw new ArgumentException("complete_phase needs open clock + phase — start first; complete_phase act | session phase via cdp_context");
        return new
        {
            op = "complete_phase",
            task_id = id,
            phase = used,
            hint = "wall phase segment end — SA diagnostic, not a score"
        };
    }

    static object TaskEvents(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveClockStageId(store, state, args)
                 ?? throw new ArgumentException("events needs active task or title");
        return store.StageEventList(state, id);
    }

    static object TaskEventNote(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveNoteStageId(store, state, args)
                 ?? throw new ArgumentException("note needs active task — focus first");
        // Prefer text=/body=; title= is legacy REPL stuffing of note body (not a stage name).
        var text = Opt(args, "text") ?? Opt(args, "body") ?? OptGoArg(args, "text") ?? OptGoArg(args, "body") ?? "";
        if (text.Length == 0)
            text = Title(args);
        return store.StageEventNote(state, id, text);
    }

    static Guid? ResolveNoteStageId(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        // id → active focus. Never treat title as stage name (note body historically lived in title=).
        _ = store;
        var id = GuidArg(args, "stage_id") ?? GuidArg(args, "task_id")
                 ?? GuidArgGo(args, "stage_id") ?? GuidArgGo(args, "task_id");
        if (id is not null)
            return id;
        return state.ActiveStageId;
    }

    static Guid? ResolveClockStageId(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args) =>
        ResolveStageTarget(store, state, args);
}
