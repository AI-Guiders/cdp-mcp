#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static bool IsPromoteOp(string op) =>
        op is "promote" or "promote_plan" or "ask_confirm"
            or "share" or "share_plan"
            or "confirm" or "plan_confirm" or "approved"
            or "reject" or "plan_reject" or "denied";

    static bool IsShareReportOp(string op, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (op is "report" or "digest" or "share_report" or "status_report")
            return true;
        if (op is not ("share" or "share_plan"))
            return false;
        var what = (Opt(args, "what") ?? OptGoArg(args, "what") ?? "").Trim().ToLowerInvariant();
        return what is "report" or "digest" or "status";
    }

    static object? Dispatch(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args,
        string op) =>
        op switch
        {
            "board" or "tasks" or "plan" or "status" or "scene" => null,
            "feature" or "intent" or "feature_add" => FeatureAdd(store, state, Title(args)),
            "feature_focus" or "intent_select" => FeatureFocus(store, state, args),
            "feature_drop" or "feature_rm" or "feature_delete" => FeatureDrop(store, state, args),
            "task" or "task_add" or "add" => TaskAdd(
                store, state, Title(args), ResolveParent(store, state, args), PhaseArg(args)),
            "focus" or "task_focus" => TaskFocus(store, state, args),
            "task_drop" or "task_rm" or "task_delete" => TaskDrop(store, state, args),
            "drop" or "rm" or "delete" => DropSmart(store, state, args),
            "done" or "complete" => TaskDone(store, state, args),
            "pending" or "reopen" => TaskStatus(store, state, args, "pending"),
            "park" or "parked" => TaskStatus(store, state, args, "parked"),
            "active" => TaskStatus(store, state, args, "active"),
            "phase" or "task_phase" => TaskSetPhase(store, state, args),
            "start" or "clock_start" => TaskClockStart(store, state, args),
            "shipped" or "completed" => TaskClockShipped(store, state, args),
            "promote" or "promote_plan" or "ask_confirm"
                or "share" or "share_plan"
                or "report" or "digest" or "share_report" or "status_report" =>
                IsShareReportOp(op, args)
                    ? IdeShare.ShareReport(
                        store, state,
                        Opt(args, "project_root") ?? OptGoArg(args, "project_root"),
                        Opt(args, "notes") ?? OptGoArg(args, "notes") ?? Opt(args, "body") ?? OptGoArg(args, "body"),
                        Opt(args, "dir") ?? OptGoArg(args, "dir") ?? Opt(args, "inbox") ?? OptGoArg(args, "inbox"),
                        Opt(args, "session_phase") ?? OptGoArg(args, "session_phase"))
                    : IdeShare.SharePlan(
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
                $"unknown task op '{op}'. Use board|feature|task|focus|done|park|drop|start|shipped|share|report|promote|confirm|reject.")
        };
}
