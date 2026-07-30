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
        string? phaseAffinity,
        string? product = null)
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
            ApplyProductIfPresent(store, state, existing, product);
            return new
            {
                op = "task_focus",
                task_id = existing,
                title,
                parent_id = parentId,
                phase_affinity = phaseAffinity,
                product = IntentWorkspaceStore.NormalizeProduct(product),
                deduped = true
            };
        }

        var r = store.StageUpsert(state, title, null, parentId, null, phaseAffinity);
        ApplyProductIfPresent(store, state, r.stage_id, product);
        store.FocusStage(state, r.stage_id);
        return new
        {
            op = "task",
            task_id = r.stage_id,
            title = r.title,
            parent_id = r.parent_id,
            phase_affinity = r.phase_affinity,
            product = IntentWorkspaceStore.NormalizeProduct(product)
        };
    }

    /// <summary>
    /// Capture backlog without stealing focus: create/find stage as <c>deferred</c> or <c>parked</c>.
    /// Bare <c>defer</c>/<c>park</c> marks the active task and restores next pending focus.
    /// </summary>
    static object TaskDefer(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args) =>
        TaskSeedBacklog(store, state, args, "deferred");

    static object TaskSeedBacklog(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args,
        string status)
    {
        if (status is not ("deferred" or "parked"))
            throw new ArgumentException($"seed status must be deferred|parked, got '{status}'");

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

            object? clock = null;
            if (status == "parked")
            {
                IdeStageCycle.TryPhaseComplete();
                clock = store.StageClockParkFreeze(state, id);
            }

            store.StageSetStatus(state, id, status);
            if (keepFocus is { } prev && prev != id)
                store.FocusStage(state, prev);
            else if (keepFocus == id)
            {
                var next = store.FindNextIncompleteLeaf(state, afterStageId: id);
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

            return clock is null
                ? new
                {
                    op = status,
                    task_id = id,
                    title = resolvedTitle,
                    status,
                    focus_preserved = keepFocus,
                    hint = $"{status} seed — focus unchanged; use focus <title> when ready"
                }
                : new
                {
                    op = status,
                    task_id = id,
                    title = resolvedTitle,
                    status,
                    focus_preserved = keepFocus,
                    clock,
                    hint = $"{status} seed — focus unchanged; use focus <title> when ready"
                };
        }

        return TaskStatus(store, state, args, status);
    }

    static object TaskSetPhase(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var phase = PhaseArg(args)
                    ?? throw new ArgumentException("phase needs value — phase act | task phase act");
        var title = Title(args);
        var id = ResolveStageTarget(store, state, args);
        if (id is null)
            throw new ArgumentException(title.Length > 0
                ? $"task not found: {title}"
                : "no active task — focus <task> first");

        var r = store.StageUpsert(state, title: "", id, parentId: null, sceneName: null, phase);
        return new { op = "phase", task_id = r.stage_id, phase_affinity = r.phase_affinity };
    }

    /// <summary>Solo plateau: mark focus @handoff (if any) and latch Autoi awaiting_operator.</summary>
    static object TaskAwaitOperator(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        object? phase = null;
        if (state.ActiveStageId is { } sid)
        {
            var r = store.StageUpsert(state, title: "", sid, parentId: null, sceneName: null, phaseAffinity: "handoff");
            phase = new { task_id = r.stage_id, phase_affinity = r.phase_affinity };
        }

        var taskLabel = state.ActiveStageId is not null
            ? store.TaskManagerSnapshot(state).ActiveStageTitle ?? "epic closed — await operator"
            : (Opt(args, "task") ?? OptGoArg(args, "task") ?? "epic closed — await operator");

        var ignite = IdeIgniteArmHost.AwaitOperator(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["task"] = JsonSerializer.SerializeToElement(taskLabel)
        });

        return new
        {
            op = "await_operator",
            phase,
            ignite,
            flight = ProbeContinuityFlight(store, state).ToString(),
            hint = "Epic closed latch set. Do not invent next epic; wait for operator. cdp_ignite op=resume after pick."
        };
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
        var leaf = TryLeafIgniteAfterFocus(store, state, "task_focus", preferredStageId: id);
        return leaf is null
            ? new { op = "focus", task_id = id }
            : new { op = "focus", task_id = state.ActiveStageId ?? id, leaf_continuity = leaf };
    }

}
