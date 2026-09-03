#nullable enable
using System.Text.Json;
using Cdp.Deploy;

namespace CdpMcp;

internal static partial class IdeDeploy
{
    static string ExecuteSuccess(
        string mode,
        string? selfRoot,
        string seat,
        TargetDecision resolved,
        IReadOnlyDictionary<string, JsonElement> args,
        CdpDeployStepResult step,
        DateTime started)
    {
        var elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
        var includeRaw = IsTruthy(args, "include_raw") || IsTruthy(args, "include_raw_output");

        object? igniteWake = null;
        object? remountWake = null;
        if (step.Ok && mode is "hard" or "apply")
        {
            try { IdeRemountWake.MarkPending(resolved.Target!, mode == "hard" ? "hard_deploy" : "apply_pending"); }
            catch { /* best-effort */ }
            if (mode == "hard")
            {
                try { igniteWake = IdeIgniteArmHost.WakeAfterHardDeploy(); }
                catch { /* best-effort */ }
            }

            var targetSeat = mode == "apply" ? "cdp" : ClassifySeat(resolved.Target);
            remountWake = new
            {
                pending_seat = targetSeat,
                pending_path = IdeRemountWake.PendingPathForSeat(targetSeat),
                hint = mode == "apply"
                    ? "Service restarted from staged .next — bump bridge remount if tools stale (CDP_RELOAD_NUDGE)."
                    : "Target MCP boot consumes pending → Autoi 'MCP remounted / initialized'"
            };
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = step.Ok,
            op = "deploy",
            pulse = step.Pulse,
            mode,
            self = selfRoot,
            seat,
            target = resolved.Target,
            sibling = resolved.Sibling,
            engine = "cdp.deploy/csharp",
            exit_code = step.ExitCode,
            elapsed_ms = elapsedMs,
            ignite_wake = igniteWake,
            remount_wake = remountWake,
            stderr_tail = includeRaw || !step.Ok ? step.Stderr : null,
            next = step.Ok
                ? new object[]
                {
                    new { go = "health", label = "cdp_health", why = "confirm version after remount" },
                    new { go = "ignite_desk", label = "Ignite arms", why = "survivor reclaim; target remount Autoi initialized" },
                    new { go = "cockpit", label = "Desk", why = "reorient after deploy" }
                }
                : null,
            hint = step.Ok
                ? mode switch
                {
                    "hard" => "Hard deploy done (C# orchestrator). cdp_health should show live version.",
                    "apply" => "Pending staged update applied (.next → live). cdp_health pending_update should be null.",
                    _ => "Soft staged (.next + pending_update). Apply with mode=apply."
                }
                : "Deploy failed — see stderr_tail."
        }, Pretty);
    }

    static string ExecuteFailure(
        string mode,
        string? selfRoot,
        string seat,
        TargetDecision resolved,
        string error,
        DateTime started) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = false,
            op = "deploy",
            pulse = $"deploy {mode} fail",
            mode,
            self = selfRoot,
            seat,
            target = resolved.Target,
            engine = "cdp.deploy/csharp",
            exit_code = 1,
            elapsed_ms = (int)(DateTime.UtcNow - started).TotalMilliseconds,
            stderr_tail = error,
            hint = "Deploy failed in C# orchestrator."
        }, Pretty);

    static string Fail(string mode, string? selfRoot, string seat, string? target, string error, string? hint) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = false,
            op = "deploy",
            error,
            mode,
            self = selfRoot,
            seat,
            target,
            hint
        }, Pretty);

    static string DryRunPayload(
        string mode,
        string? selfRoot,
        string seat,
        TargetDecision resolved,
        CdpDeployPlanResult planResult) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = planResult.Ok,
            op = "deploy",
            dry_run = true,
            mode,
            self = selfRoot,
            seat,
            target = resolved.Target,
            sibling = resolved.Sibling,
            engine = "cdp.deploy/csharp",
            plan = planResult.Ok
                ? new
                {
                    service_publish = planResult.Plan!.ServicePublishRoot,
                    bridge_publish = planResult.Plan.BridgePublishRoot,
                    bridge_debug_publish = planResult.Plan.BridgeDebugPublishRoot
                }
                : null,
            error = planResult.Error,
            hint = planResult.Ok
                ? "dry_run — no process started. Drop dry_run= to execute."
                : planResult.Hint
        }, Pretty);
}
