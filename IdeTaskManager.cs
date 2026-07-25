using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Agent Task Manager (MLP) — Feature=Intent, Task=Stage tree; sticky focus in WitDB.
/// Survives MCP remount like seats. Desk organ <c>go=plan</c> (aliases: work|tasks|tm|feature|task).
/// </summary>
internal static class IdeTaskManager
{
    public const string SchemaVersion = "task_manager/v1.2";

    public static object Handle(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = Opt(args, "tm_op")
                 ?? OptGoArg(args, "op")
                 ?? Opt(args, "op")
                 ?? "board";
        op = op.Trim().ToLowerInvariant();

        object? mutation = null;
        try
        {
            mutation = op switch
            {
                "board" or "tasks" or "plan" or "status" or "scene" => null,
                "feature" or "intent" or "feature_add" => FeatureAdd(store, state, Title(args)),
                "feature_focus" or "intent_select" => FeatureFocus(store, state, args),
                "feature_drop" or "feature_rm" or "feature_delete" => FeatureDrop(store, state, args),
                "task" or "task_add" or "add" => TaskAdd(store, state, Title(args), ResolveParent(store, state, args)),
                "focus" or "task_focus" => TaskFocus(store, state, args),
                "task_drop" or "task_rm" or "task_delete" => TaskDrop(store, state, args),
                "drop" or "rm" or "delete" => DropSmart(store, state, args),
                "done" or "complete" => TaskDone(store, state, args),
                "pending" or "reopen" => TaskStatus(store, state, args, "pending"),
                "park" or "parked" => TaskStatus(store, state, args, "parked"),
                "active" => TaskStatus(store, state, args, "active"),
                "promote" or "promote_plan" or "ask_confirm"
                or "share" or "share_plan" => IdeShare.SharePlan(
                    store, state, Opt(args, "project_root") ?? OptGoArg(args, "project_root"),
                    Opt(args, "notes") ?? OptGoArg(args, "notes") ?? Opt(args, "body") ?? OptGoArg(args, "body"),
                    Opt(args, "dir") ?? OptGoArg(args, "dir") ?? Opt(args, "inbox") ?? OptGoArg(args, "inbox"),
                    Opt(args, "ask") ?? OptGoArg(args, "ask")),
                "confirm" or "plan_confirm" or "approved" => IdePlanPromote.Confirm(
                    store, state, Opt(args, "project_root") ?? OptGoArg(args, "project_root"),
                    Opt(args, "dir") ?? OptGoArg(args, "dir"),
                    Opt(args, "plan_id") ?? OptGoArg(args, "plan_id"),
                    reject: false),
                "reject" or "plan_reject" or "denied" => IdePlanPromote.Confirm(
                    store, state, Opt(args, "project_root") ?? OptGoArg(args, "project_root"),
                    Opt(args, "dir") ?? OptGoArg(args, "dir"),
                    Opt(args, "plan_id") ?? OptGoArg(args, "plan_id"),
                    reject: true),
                _ => throw new ArgumentException(
                    $"unknown task op '{op}'. Use board|feature|task|focus|done|park|drop|share|promote|confirm|reject.")
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "task_manager",
                error = ex.Message,
                hint =
                    "cmd=\"feature X\" | task Y | share | promote | confirm | reject | drop | done | go=plan"
            };
        }

        var board = BuildBoard(store, state);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "task_manager",
            go = "plan",
            detail = "pulse",
            pulse = board.Pulse,
            view = board.View,
            focus = board.Focus,
            mutation,
            promoted = op is "promote" or "promote_plan" or "ask_confirm"
                or "share" or "share_plan"
                or "confirm" or "plan_confirm" or "approved"
                or "reject" or "plan_reject" or "denied"
                ? mutation
                : IdePlanPromote.TryPulse(
                    Opt(args, "project_root") ?? OptGoArg(args, "project_root"),
                    Opt(args, "dir") ?? OptGoArg(args, "dir")),
            hint =
                "Feature=Intent, Task=Stage (WitDB). Sticky focus survives remount. " +
                "REPL: feature|task|focus|done|park|drop|share|promote|confirm|reject|plan. pane_full=plan for JSON tree."
        };
    }

    public static string PulseLine(IntentWorkspaceStore? store, IntentWorkspaceState state)
    {
        if (store is null)
            return "no task store";
        try
        {
            return BuildBoard(store, state).Pulse;
        }
        catch
        {
            return "task manager error";
        }
    }

    public static Board BuildBoard(IntentWorkspaceStore store, IntentWorkspaceState state)
    {
        var snap = store.TaskManagerSnapshot(state);
        var lines = new List<string>();
        foreach (var feature in snap.Features)
        {
            var mark = feature.IsActive ? "*" : " ";
            lines.Add($"{mark}{feature.Title}");
            // Active feature: full tree. Others: collapsed (title only) to keep board scannable.
            if (!feature.IsActive)
                continue;
            foreach (var line in FormatStageTree(feature.Stages, feature.ActiveStageId, indent: 0))
                lines.Add(line);
        }

        if (lines.Count == 0)
            lines.Add("(empty — cmd=\"feature <name>\")");

        var pulse = snap.ActiveFeatureTitle is { Length: > 0 } f
            ? snap.ActiveStageTitle is { Length: > 0 } t
                ? $"{f} › {t}"
                : $"{f} › (pick task)"
            : "no plan — feature <name>";

        var banner = snap.ActiveFeatureTitle is { Length: > 0 }
            ? $"| plan:{Trim(snap.ActiveFeatureTitle, 18)} | task:{Trim(snap.ActiveStageTitle ?? "—", 18)} |"
            : "| plan:— | task:— |";

        return new Board(
            Pulse: pulse,
            View: new
            {
                schema = SchemaVersion,
                banner,
                board = lines.ToArray(),
                ascii = string.Join('\n', lines),
                hint = "Scan board. * = active feature; [>] active task; [x] done; [ ] pending."
            },
            Focus: new
            {
                feature_id = snap.ActiveFeatureId,
                feature = snap.ActiveFeatureTitle,
                task_id = snap.ActiveStageId,
                task = snap.ActiveStageTitle
            });
    }

    static IEnumerable<string> FormatStageTree(
        IReadOnlyList<StageNode> stages,
        Guid? activeStageId,
        int indent)
    {
        var roots = stages.Where(s => s.ParentId is null).OrderBy(s => s.Ordinal).ToList();
        foreach (var root in roots)
        {
            foreach (var line in Walk(root, stages, activeStageId, indent))
                yield return line;
        }

        // Orphans (parent missing) — still show
        var ids = stages.Select(s => s.Id).ToHashSet();
        foreach (var orphan in stages.Where(s => s.ParentId is { } p && !ids.Contains(p)).OrderBy(s => s.Ordinal))
        {
            foreach (var line in Walk(orphan, stages, activeStageId, indent))
                yield return line;
        }
    }

    static IEnumerable<string> Walk(
        StageNode node,
        IReadOnlyList<StageNode> all,
        Guid? activeStageId,
        int indent)
    {
        var pad = new string(' ', indent * 2);
        var box = node.Status.Equals("done", StringComparison.OrdinalIgnoreCase) ? "[x]"
            : node.Status.Equals("parked", StringComparison.OrdinalIgnoreCase) ? "[-]"
            : activeStageId == node.Id || node.Status.Equals("active", StringComparison.OrdinalIgnoreCase) ? "[>]"
            : "[ ]";
        yield return $"{pad}|--- {box} {node.Title}";
        foreach (var child in all.Where(s => s.ParentId == node.Id).OrderBy(s => s.Ordinal))
        {
            foreach (var line in Walk(child, all, activeStageId, indent + 1))
                yield return line;
        }
    }

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
        return new { op = "feature_focus", feature_id = r.intent_id, title = r.title };
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

    static object TaskAdd(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string title,
        Guid? parentId)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("task needs title — task ship-omit");
        if (state.ActiveIntentId is null)
            throw new ArgumentException("no active feature — feature <name> first");

        if (store.FindStageMatching(state, title, parentId, matchParent: true) is { } existing)
        {
            store.FocusStage(state, existing);
            return new { op = "task_focus", task_id = existing, title, parent_id = parentId, deduped = true };
        }

        var r = store.StageUpsert(state, title, null, parentId, null);
        store.FocusStage(state, r.stage_id);
        return new { op = "task", task_id = r.stage_id, title = r.title, parent_id = r.parent_id };
    }

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
            // Advance to next pending under same feature
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

    static string Title(IReadOnlyDictionary<string, JsonElement> args)
    {
        var t = Opt(args, "title") ?? OptGoArg(args, "title") ?? Opt(args, "name") ?? OptGoArg(args, "name");
        if (t is { Length: > 0 })
            return t;
        // Positional leftovers from REPL go_args.q
        return OptGoArg(args, "q") ?? Opt(args, "q") ?? "";
    }

    static Guid? ResolveParent(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var guid = GuidArg(args, "parent_id")
                   ?? GuidArgGo(args, "parent_id")
                   ?? GuidArg(args, "parent")
                   ?? GuidArgGo(args, "parent");
        if (guid is not null)
            return guid;

        var under = Opt(args, "under")
                    ?? OptGoArg(args, "under")
                    ?? Opt(args, "parent_title")
                    ?? OptGoArg(args, "parent_title");
        // Bare parent= as title when not a GUID (GuidArg already returned null).
        under ??= Opt(args, "parent") ?? OptGoArg(args, "parent");
        if (under is not { Length: > 0 })
            return null;

        return store.FindStageIdByTitle(state, under)
               ?? throw new ArgumentException($"parent Task not found: {under}");
    }

    static Guid? GuidArg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        var s = Opt(args, key);
        return Guid.TryParse(s, out var g) ? g : null;
    }

    static Guid? GuidArgGo(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        var s = OptGoArg(args, key);
        return Guid.TryParse(s, out var g) ? g : null;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static string? OptGoArg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue("go_args", out var ga) || ga.ValueKind != JsonValueKind.Object)
            return null;
        if (!ga.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    public readonly record struct Board(string Pulse, object View, object Focus);

    public sealed record StageNode(
        Guid Id,
        Guid? ParentId,
        string Title,
        string Status,
        int Ordinal);

    public sealed record FeatureNode(
        Guid Id,
        string Title,
        bool IsActive,
        Guid? ActiveStageId,
        IReadOnlyList<StageNode> Stages);

    public sealed record Snapshot(
        Guid? ActiveFeatureId,
        string? ActiveFeatureTitle,
        Guid? ActiveStageId,
        string? ActiveStageTitle,
        IReadOnlyList<FeatureNode> Features);
}
