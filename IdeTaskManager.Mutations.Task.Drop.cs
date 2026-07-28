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
        var title = Title(args);
        var id = ResolveStageTarget(store, state, args);
        if (id is null)
            throw new ArgumentException(title.Length > 0
                ? $"task not found: {title}"
                : "drop task needs title, id, or active focus");

        return store.StageDelete(state, id.Value);
    }

    /// <summary>
    /// <c>drop</c> without kind: prefer title Task, else Feature, else active Task.
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

        if (title.Length > 0)
            throw new ArgumentException($"task/feature not found: {title}");

        if (state.ActiveStageId is not null)
            return TaskDrop(store, state, args);

        throw new ArgumentException("drop needs task/feature title — drop task X | drop feature Y");
    }
}
