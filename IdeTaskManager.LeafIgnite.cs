#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    /// <summary>
    /// After focus settles: ensure ActiveStageId is an incomplete leaf, then arm AutoI for it.
    /// Returns null when no incomplete leaf exists.
    /// </summary>
    static object? TryLeafIgniteAfterFocus(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string reason,
        Guid? preferredStageId = null)
    {
        Guid? leafId = null;
        if (preferredStageId is { } pref)
            leafId = store.ResolveIncompleteLeaf(state, pref);

        leafId ??= state.ActiveStageId is { } cur
            ? store.ResolveIncompleteLeaf(state, cur)
            : store.FindFirstIncompleteLeaf(state);

        leafId ??= store.FindFirstIncompleteLeaf(state);

        if (leafId is null)
            return null;

        if (state.ActiveStageId != leafId)
            store.FocusStage(state, leafId.Value);

        var title = store.StageTitle(state, leafId.Value);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var ignite = IdeIgniteArmHost.ArmForLeaf(title, reason);
        return new
        {
            leaf_id = leafId,
            leaf_title = title,
            reason,
            ignite
        };
    }

    /// <summary>Done on last leaf: clear focus and latch await_operator.</summary>
    static object LeafPlateau(IntentWorkspaceStore store, IntentWorkspaceState state, string reason)
    {
        state.ActiveStageId = null;
        store.WorkFocusSave(state);
        var ignite = IdeIgniteArmHost.AwaitOperator(new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["task"] = System.Text.Json.JsonSerializer.SerializeToElement("feature leaves complete — await operator")
        });
        return new
        {
            leaf_id = (Guid?)null,
            leaf_title = (string?)null,
            reason,
            plateau = true,
            ignite
        };
    }
}
