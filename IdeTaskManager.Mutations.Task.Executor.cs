#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskSetExecutor(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var executor = ExecutorArg(args)
                       ?? throw new ArgumentException("executor needs value — executor Sierra | assignee Кир | executor clear");
        var title = Title(args);
        var id = ResolveStageTarget(store, state, args);
        if (id is null)
            throw new ArgumentException(title.Length > 0
                ? $"task not found: {title}"
                : "no active task — focus <task> first");

        var r = store.StageSetExecutor(state, id.Value, executor);
        return new { op = "executor", task_id = r.stage_id, executor = r.executor };
    }

    static string? ExecutorArg(IReadOnlyDictionary<string, JsonElement> args) =>
        Opt(args, "executor")
        ?? OptGoArg(args, "executor")
        ?? Opt(args, "assignee")
        ?? OptGoArg(args, "assignee");

    static void ApplyExecutorIfPresent(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        Guid stageId,
        string? executor)
    {
        if (executor is null)
            return;
        store.StageSetExecutor(state, stageId, executor);
    }
}
