#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>cdp_work op switch peeled from Program (soft-warn).</summary>
internal static class CdpWorkDispatch
{
    public static object Dispatch(CdpWorkDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        var store = d.RequireWorkspace();
        var workspaceState = d.WorkspaceState;
        var session = d.Session;
        var NotifyListChanged = d.NotifyListChanged;

        if (!callArgs.TryGetValue("op", out var opEl) || opEl.GetString() is not { Length: > 0 } op)
            throw new ArgumentException("op is required for cdp_work.");
        op = op.Trim().ToLowerInvariant();

        string? Str(string key) =>
            callArgs.TryGetValue(key, out var el) && el.GetString() is { Length: > 0 } s ? s.Trim() : null;
        Guid? GuidArg(string key)
        {
            var s = Str(key);
            if (s is null) return null;
            return Guid.TryParse(s, out var g) ? g : throw new ArgumentException($"{key} must be a GUID.");
        }
        int? IntArg(string key)
        {
            if (!callArgs.TryGetValue(key, out var el) || !el.TryGetInt32(out var n)) return null;
            return n;
        }

        var sceneName = Str("name") ?? Str("scene_name");

        return op switch
        {
            "intent_upsert" => store.IntentUpsert(workspaceState, Str("title") ?? "", GuidArg("intent_id")),
            "intent_list" => store.IntentList(),
            "intent_select" => store.IntentSelect(
                workspaceState,
                GuidArg("intent_id") ?? throw new ArgumentException("intent_id is required for intent_select.")),
            "stage_upsert" => store.StageUpsert(
                workspaceState, Str("title") ?? "", GuidArg("stage_id"), GuidArg("parent_id"), sceneName),
            "stage_list" => store.StageList(workspaceState),
            "stage_set_status" => store.StageSetStatus(
                workspaceState,
                GuidArg("stage_id") ?? throw new ArgumentException("stage_id is required."),
                Str("status") ?? throw new ArgumentException("status is required.")),
            "stage_enqueue" => EnqueueStageJob(d, store, Str("title") ?? "", Str("job_json"), callArgs),
            "stage_get" => store.StageGet(
                GuidArg("stage_id") ?? throw new ArgumentException("stage_id is required for stage_get.")),
            "scene_park" => store.ScenePark(
                workspaceState, session,
                sceneName ?? throw new ArgumentException("name (or scene_name) is required for scene_park."),
                Str("loot"), Str("focus_path"), IntArg("focus_line"), GuidArg("bind_stage_id")),
            "scene_switch" => store.SceneSwitch(
                workspaceState, session,
                sceneName ?? throw new ArgumentException("name (or scene_name) is required for scene_switch."),
                NotifyListChanged),
            "scene_list" => store.SceneList(workspaceState),
            "status" => store.Status(workspaceState, session),
            "tasks" or "board" or "plan" or "feature" or "task" or "focus" or "done"
                or "park" or "defer" or "deferred" or "pending" or "active" or "drop" or "rm" or "delete"
                or "feature_drop" or "task_drop"
                or "criteria" or "criterion" or "criterion_list" or "criterion_add"
                or "criterion_met" or "criterion_unmet" or "criterion_waived" or "criterion_pending"
                or "criterion_status" or "criterion_drop"
                or "promote" or "promote_plan" or "ask_confirm"
                or "share" or "share_plan"
                or "confirm" or "plan_confirm" or "approved"
                or "reject" or "plan_reject" or "denied" => IdeTaskManager.Handle(
                store,
                workspaceState,
                MergeTmOp(InjectProjectRoot(callArgs, session), op)),
            "intent_delete" => store.IntentDelete(
                workspaceState,
                GuidArg("intent_id") ?? throw new ArgumentException("intent_id is required for intent_delete.")),
            "stage_delete" => store.StageDelete(
                workspaceState,
                GuidArg("stage_id") ?? throw new ArgumentException("stage_id is required for stage_delete.")),
            _ => throw new ArgumentException(
                $"Unknown cdp_work op '{op}'. Use intent_*|stage_*|criterion_*|scene_*|status|tasks|feature|task|focus|done|drop.")
        };
    }

    static IReadOnlyDictionary<string, JsonElement> MergeTmOp(
        IReadOnlyDictionary<string, JsonElement> callArgs,
        string op)
    {
        var dict = new Dictionary<string, JsonElement>(callArgs, StringComparer.Ordinal)
        {
            ["tm_op"] = JsonSerializer.SerializeToElement(op is "tasks" or "board" or "plan" or "status" ? "board" : op)
        };
        return dict;
    }

    static IReadOnlyDictionary<string, JsonElement> InjectProjectRoot(
        IReadOnlyDictionary<string, JsonElement> callArgs,
        SessionContext session)
    {
        if (callArgs.TryGetValue("project_root", out var existing)
            && existing.ValueKind == JsonValueKind.String
            && existing.GetString() is { Length: > 0 })
            return callArgs;
        if (session.ProjectRoot is not { Length: > 0 } pr)
            return callArgs;
        var dict = new Dictionary<string, JsonElement>(callArgs, StringComparer.Ordinal)
        {
            ["project_root"] = JsonSerializer.SerializeToElement(pr)
        };
        return dict;
    }

    static object EnqueueStageJob(
        CdpWorkDispatchDeps d,
        IntentWorkspaceStore store,
        string title,
        string? jobJson,
        IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        if (string.IsNullOrWhiteSpace(jobJson))
            throw new ArgumentException("job_json is required for stage_enqueue.");
        using var doc = JsonDocument.Parse(jobJson);
        var root = doc.RootElement;
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in root.EnumerateObject())
            dict[p.Name] = p.Value.Clone();
        if ((!dict.ContainsKey("solution_or_project_path")
             || dict["solution_or_project_path"].ValueKind != JsonValueKind.String
             || string.IsNullOrWhiteSpace(dict["solution_or_project_path"].GetString()))
            && d.Session.SolutionOrProjectPath is { Length: > 0 } sol)
        {
            dict["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol);
        }

        var enriched = JsonSerializer.Serialize(dict);
        var created = store.StageEnqueue(d.WorkspaceState, title, enriched);
        var start = true;
        if (callArgs.TryGetValue("start_job", out var sj) && sj.ValueKind == JsonValueKind.False)
            start = false;
        if (start)
        {
            using var cdoc = JsonDocument.Parse(JsonSerializer.Serialize(created));
            var stageId = cdoc.RootElement.GetProperty("stage_id").GetGuid();
            d.RequireJobRunner().Enqueue(stageId, enriched);
        }

        return created;
    }
}

internal sealed class CdpWorkDispatchDeps
{
    public required SessionContext Session { get; init; }
    public required IntentWorkspaceState WorkspaceState { get; init; }
    public required Func<IntentWorkspaceStore> RequireWorkspace { get; init; }
    public required Func<IdeReportJobRunner> RequireJobRunner { get; init; }
    public required Action NotifyListChanged { get; init; }
}
