#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object FeatureAdd(IntentWorkspaceStore store, IntentWorkspaceState state, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("feature needs title — feature desk-comfort");
        if (store.FindIntentIdByTitle(title) is { } existing)
        {
            var focused = store.IntentSelect(state, existing);
            state.ActiveStageId = null;
            store.WorkFocusSave(state);
            return new { op = "feature_focus", feature_id = focused.intent_id, title = focused.title, deduped = true };
        }

        var r = store.IntentUpsert(state, title, null);
        store.WorkFocusSave(state);
        return new { op = "feature", feature_id = r.intent_id, title = r.title };
    }

        static object FeatureFocus(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = GuidArg(args, "intent_id") ?? GuidArg(args, "feature_id");
        if (id is null)
        {
            var title = Title(args);
            id = store.FindIntentIdByTitle(title)
                 ?? throw new ArgumentException($"feature not found: {title}");
        }

        var r = store.IntentSelect(state, id.Value);
        state.ActiveStageId = null;
        store.WorkFocusSave(state);

        var leaf = TryLeafIgniteAfterFocus(store, state, "feature_focus", preferredStageId: null);
        return leaf is null
            ? new { op = "feature_focus", feature_id = r.intent_id, title = r.title }
            : new { op = "feature_focus", feature_id = r.intent_id, title = r.title, leaf_continuity = leaf };
    }


    static object FeatureDrop(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = GuidArg(args, "intent_id") ?? GuidArg(args, "feature_id")
                 ?? GuidArgGo(args, "intent_id") ?? GuidArgGo(args, "feature_id");
        if (id is null)
        {
            var title = Title(args);
            if (title.Length == 0)
                throw new ArgumentException("drop feature needs title or feature_id");
            id = store.FindIntentIdByTitle(title)
                 ?? throw new ArgumentException($"feature not found: {title}");
        }

        return store.IntentDelete(state, id.Value);
    }
}
