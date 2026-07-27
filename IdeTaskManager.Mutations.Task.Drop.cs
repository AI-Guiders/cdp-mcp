#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskDrop(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = GuidArg(args, "stage_id") ?? GuidArg(args, "task_id")
                 ?? GuidArgGo(args, "stage_id") ?? GuidArgGo(args, "task_id")
                 ?? state.ActiveStageId;
        if (id is null)
        {
            var title = Title(args);
            if (title.Length == 0)
                throw new ArgumentException("drop task needs title, id, or active focus");
            id = store.FindStageIdByTitle(state, title)
                 ?? throw new ArgumentException($"task not found: {title}");
        }

        return store.StageDelete(state, id.Value);
    }

    /// <summary>
    /// <c>drop</c> without kind: prefer active/title Task, else Feature.
    /// Kind via go_args.kind=feature|task or title prefix already handled in REPL.
    /// </summary>
    static object DropSmart(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var kind = (Opt(args, "kind") ?? OptGoArg(args, "kind") ?? "").Trim().ToLowerInvariant();
        if (kind is "feature" or "intent")
            return FeatureDrop(store, state, args);
        if (kind is "task" or "stage")
            return TaskDrop(store, state, args);

        var title = Title(args);
        if (title.Length == 0 && state.ActiveStageId is not null)
            return TaskDrop(store, state, args);

        if (title.Length > 0 && store.FindStageIdByTitle(state, title) is not null)
            return TaskDrop(store, state, args);

        if (title.Length > 0 && store.FindIntentIdByTitle(title) is not null)
            return FeatureDrop(store, state, args);

        if (state.ActiveStageId is not null)
            return TaskDrop(store, state, args);

        throw new ArgumentException("drop needs task/feature title — drop task X | drop feature Y");
    }
}
