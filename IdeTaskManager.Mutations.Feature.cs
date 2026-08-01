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
            // Dedupe path = feature_focus + leaf-arm (dogfood gap after 0.5.309).
            var focused = store.IntentSelect(state, existing);
            state.ActiveStageId = null;
            store.WorkFocusSave(state);
            var leaf = TryLeafIgniteAfterFocus(store, state, "feature_focus", preferredStageId: null);
            return leaf is null
                ? new { op = "feature_focus", feature_id = focused.intent_id, title = focused.title, deduped = true }
                : new
                {
                    op = "feature_focus",
                    feature_id = focused.intent_id,
                    title = focused.title,
                    deduped = true,
                    leaf_continuity = leaf
                };
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
            var t = Title(args);
            id = store.FindIntentIdByTitle(t)
                 ?? throw new ArgumentException($"feature not found: {t}");
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
            var t = Title(args);
            if (t.Length == 0)
                throw new ArgumentException("drop feature needs title or feature_id");
            id = store.FindIntentIdByTitle(t)
                 ?? throw new ArgumentException($"feature not found: {t}");
        }

        return store.IntentDelete(state, id.Value);
    }

    /// <summary>
    /// Resolve feature like DropSmart: kind=feature | title match | active feature when no stage focus.
    /// </summary>
    static Guid? TryResolveFeatureId(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var kind = (Opt(args, "kind") ?? OptGoArg(args, "kind") ?? "").Trim().ToLowerInvariant();
        if (kind is "feature" or "intent")
        {
            var id = GuidArg(args, "intent_id") ?? GuidArg(args, "feature_id")
                     ?? GuidArgGo(args, "intent_id") ?? GuidArgGo(args, "feature_id");
            if (id is not null)
                return id;
            var t = Title(args);
            if (t.Length > 0)
                return store.FindIntentIdByTitle(t);
            return state.ActiveIntentId;
        }

        var title = Title(args);
        if (title.Length > 0)
            return store.FindIntentIdByTitle(title);

        // Bare done/shipped with feature focus and no active task.
        if (state.ActiveStageId is null && state.ActiveIntentId is { } aid)
            return aid;

        return null;
    }

    /// <summary>Close remaining incomplete leaves under a feature, then plateau.</summary>
    static object FeatureDone(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        Guid featureId,
        string reason)
    {
        store.IntentSelect(state, featureId);
        var closed = store.MarkIncompleteStagesDone(state);
        state.ActiveStageId = null;
        state.ActiveIntentId = null;
        store.WorkFocusSave(state);
        var leaf = LeafPlateau(store, state, $"feature_{reason}");
        return new
        {
            op = $"feature_{reason}",
            feature_id = featureId,
            closed_tasks = closed,
            leaf_continuity = leaf,
            hint = closed > 0
                ? "Feature closed — incomplete leaves marked done; focus cleared."
                : "Feature had no incomplete leaves — focus cleared."
        };
    }
}
