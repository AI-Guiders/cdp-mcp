#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskDone(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = GuidArg(args, "stage_id") ?? GuidArg(args, "task_id")
                 ?? GuidArgGo(args, "stage_id") ?? GuidArgGo(args, "task_id")
                 ?? state.ActiveStageId;
        if (id is null)
        {
            var title = Title(args);
            if (title.Length > 0)
                id = store.FindStageIdByTitle(state, title);
        }

        if (id is null)
            throw new ArgumentException("done needs active task or title — focus X | done X");
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
        var id = GuidArg(args, "stage_id") ?? GuidArg(args, "task_id") ?? state.ActiveStageId
                 ?? throw new ArgumentException($"{status} needs task id or active focus");
        if (status == "active")
        {
            store.FocusStage(state, id);
            return new { op = "active", task_id = id };
        }

        var r = store.StageSetStatus(state, id, status);
        store.WorkFocusSave(state);
        return new { op = status, task_id = r.stage_id, status = r.status };
    }

    static object TaskClockStart(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveClockStageId(store, state, args)
                 ?? throw new ArgumentException("start needs active task or title — focus X | start X");
        return store.StageClockStart(state, id);
    }

    static object TaskClockShipped(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveClockStageId(store, state, args)
                 ?? throw new ArgumentException("shipped needs active task or title — start first, then shipped");
        return store.StageClockShipped(state, id);
    }

    static Guid? ResolveClockStageId(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = GuidArg(args, "stage_id") ?? GuidArg(args, "task_id")
                 ?? GuidArgGo(args, "stage_id") ?? GuidArgGo(args, "task_id")
                 ?? state.ActiveStageId;
        if (id is null)
        {
            var title = Title(args);
            if (title.Length > 0)
                id = store.FindStageIdByTitle(state, title);
        }

        return id;
    }
}
