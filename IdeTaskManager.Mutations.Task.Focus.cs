#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskAdd(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string title,
        Guid? parentId,
        string? phaseAffinity)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("task needs title — task ship-omit | task ship @act");
        if (state.ActiveIntentId is null)
            throw new ArgumentException("no active feature — feature <name> first");

        if (store.FindStageMatching(state, title, parentId, matchParent: true) is { } existing)
        {
            store.FocusStage(state, existing);
            if (phaseAffinity is not null)
                store.StageUpsert(state, title: "", existing, parentId: null, sceneName: null, phaseAffinity);
            return new
            {
                op = "task_focus",
                task_id = existing,
                title,
                parent_id = parentId,
                phase_affinity = phaseAffinity,
                deduped = true
            };
        }

        var r = store.StageUpsert(state, title, null, parentId, null, phaseAffinity);
        store.FocusStage(state, r.stage_id);
        return new
        {
            op = "task",
            task_id = r.stage_id,
            title = r.title,
            parent_id = r.parent_id,
            phase_affinity = r.phase_affinity
        };
    }

    /// <summary>
    /// Capture backlog without stealing focus: create/find stage as <c>deferred</c>.
    /// Bare <c>defer</c> marks the active task deferred and restores next pending focus.
    /// </summary>
    static object TaskDefer(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var title = Title(args);
        if (title.Length > 0)
        {
            if (state.ActiveIntentId is null)
                throw new ArgumentException("no active feature — feature <name> first");

            var keepFocus = state.ActiveStageId;
            var parentId = ResolveParent(store, state, args);
            Guid id;
            string resolvedTitle;
            if (store.FindStageMatching(state, title, parentId, matchParent: true) is { } existing)
            {
                id = existing;
                resolvedTitle = title;
            }
            else
            {
                var created = store.StageUpsert(state, title, null, parentId, null, PhaseArg(args));
                id = created.stage_id;
                resolvedTitle = created.title;
            }

            store.StageSetStatus(state, id, "deferred");
            if (keepFocus is { } prev && prev != id)
                store.FocusStage(state, prev);
            else if (keepFocus == id)
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
            {
                state.ActiveStageId = null;
                store.WorkFocusSave(state);
            }

            return new
            {
                op = "deferred",
                task_id = id,
                title = resolvedTitle,
                status = "deferred",
                focus_preserved = keepFocus,
                hint = "deferred seed — focus unchanged; use focus <title> when ready"
            };
        }

        return TaskStatus(store, state, args, "deferred");
    }

    static object TaskSetPhase(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var phase = PhaseArg(args)
                    ?? throw new ArgumentException("phase needs value — phase act | task phase act");
        var id = GuidArg(args, "stage_id") ?? GuidArg(args, "task_id") ?? state.ActiveStageId;
        if (id is null)
        {
            var title = Title(args);
            if (title.Length > 0)
                id = store.FindStageIdByTitle(state, title);
        }

        if (id is null)
            throw new ArgumentException("no active task — focus <task> first");

        var r = store.StageUpsert(state, title: "", id, parentId: null, sceneName: null, phase);
        return new { op = "phase", task_id = r.stage_id, phase_affinity = r.phase_affinity };
    }

    static string? PhaseArg(IReadOnlyDictionary<string, JsonElement> args) =>
        Opt(args, "phase") ?? OptGoArg(args, "phase") ?? Opt(args, "phase_affinity") ?? OptGoArg(args, "phase_affinity");

    static object TaskFocus(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = GuidArg(args, "stage_id") ?? GuidArg(args, "task_id");
        if (id is null)
        {
            var title = Title(args);
            id = store.FindStageIdByTitle(state, title)
                 ?? throw new ArgumentException($"task not found: {title}");
        }

        store.FocusStage(state, id.Value);
        return new { op = "focus", task_id = id };
    }
}
