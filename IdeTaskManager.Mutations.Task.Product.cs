#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskSetProduct(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var product = ProductArg(args)
                      ?? throw new ArgumentException("product needs value — product CDP | category Cursor | product clear");
        var title = Title(args);
        var id = ResolveStageTarget(store, state, args);
        if (id is null)
            throw new ArgumentException(title.Length > 0
                ? $"task not found: {title}"
                : "no active task — focus <task> first");

        var r = store.StageSetProduct(state, id.Value, product);
        return new { op = "product", task_id = r.stage_id, product = r.product };
    }

    static string? ProductArg(IReadOnlyDictionary<string, JsonElement> args) =>
        Opt(args, "product")
        ?? OptGoArg(args, "product")
        ?? Opt(args, "category")
        ?? OptGoArg(args, "category");

    static void ApplyProductIfPresent(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        Guid stageId,
        string? product)
    {
        if (product is null)
            return;
        store.StageSetProduct(state, stageId, product);
    }
}
