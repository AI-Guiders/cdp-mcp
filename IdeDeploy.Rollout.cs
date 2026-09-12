#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.Deploy;

namespace CdpMcp;

internal static partial class IdeDeploy
{
    static string Rollout(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var dryRun = IsTruthy(args, "dry_run") || IsTruthy(args, "peek");
        var useNuGet = IsTruthy(args, "use_nuget") || IsTruthy(args, "UseNuGet");
        var noNudge = IsTruthy(args, "no_nudge") || IsTruthy(args, "NoNudgeMcp");
        var includeRaw = IsTruthy(args, "include_raw") || IsTruthy(args, "include_raw_output");

        var selfRoot = ResolveSelfInstallRoot();
        var seat = ClassifySeat(selfRoot);
        var sibling = CdpDeployLayout.Default.SiblingBridgeForSeat(seat);
        var selfTarget = selfRoot ?? ReleaseTarget;

        if (dryRun)
        {
            var preview = new List<object>
            {
                new { label = "soft_sibling", mode = "soft", target = sibling },
                new { label = "soft_self", mode = "soft", target = selfTarget },
                new { label = "apply_staged", mode = "apply", target = ServiceTarget }
            };

            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "rollout",
                dry_run = true,
                engine = "cdp.deploy/csharp",
                seat,
                self = selfRoot,
                steps = preview,
                hint = "Rollout = stage both seats, then apply (immutable slot + bridge). No hard_sibling KillRunning (ADR-0226)."
            }, Pretty);
        }

        if (!Monitor.TryEnter(PublishGate))
        {
            return Fail("rollout", selfRoot, seat, sibling, "deploy_in_flight",
                "Another cdp_deploy is still publishing — wait, then retry rollout.");
        }

        var steps = new List<object>();
        try
        {
            var plan = new List<(string mode, string target, string label)>
            {
                ("soft", sibling, "soft_sibling"),
                ("soft", selfTarget, "soft_self"),
                ("apply", ServiceTarget, "apply_staged")
            };

            foreach (var (mode, target, label) in plan)
            {
                var started = DateTime.UtcNow;
                var resolved = ResolveTarget(selfRoot, seat, target, mode, force: false);
                var planResult = BuildPlan(session, args, mode, selfRoot, resolved, useNuGet, noNudge, force: false);
                CdpDeployStepResult step;
                var exit = 0;
                string? stderr = null;
                try
                {
                    if (!planResult.Ok || planResult.Plan is null)
                        throw new InvalidOperationException(planResult.Hint ?? planResult.Error ?? "plan failed");
                    step = CdpDeployOrchestrator.Run(planResult.Plan!);
                    if (!step.Ok)
                    {
                        exit = step.ExitCode == 0 ? 1 : step.ExitCode;
                        stderr = step.Stderr;
                    }
                }
                catch (Exception ex)
                {
                    exit = 1;
                    step = new CdpDeployStepResult(false, $"{label} fail", null, 1, ex.Message);
                    stderr = ex.Message;
                }

                var elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                steps.Add(new
                {
                    label,
                    mode,
                    target,
                    ok = exit == 0,
                    exit_code = exit,
                    elapsed_ms = elapsedMs,
                    pulse = step.Pulse,
                    stderr_tail = includeRaw || exit != 0 ? Tail(stderr ?? "", includeRaw ? 2000 : 800) : null
                });

                if (exit != 0)
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "rollout",
                        engine = "cdp.deploy/csharp",
                        pulse = $"rollout fail at {label} exit={exit}",
                        seat,
                        self = selfRoot,
                        steps,
                        hint = "Rollout stopped on first failure. Fix, then retry mode=rollout. Routine single-seat ship: mode=ship."
                    }, Pretty);
                }
            }

            object? remountWake = null;
            try
            {
                IdeRemountWake.MarkPending(ReleaseTarget, "apply_pending");
                remountWake = new
                {
                    pending_seat = "cdp",
                    pending_path = IdeRemountWake.PendingPathForSeat("cdp"),
                    hint = "Rollout ok — service slot from snapshot; bump bridge remount if tools stale (CDP_RELOAD_NUDGE)."
                };
            }
            catch { /* best-effort */ }

            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "rollout",
                engine = "cdp.deploy/csharp",
                pulse = "rollout ok",
                seat,
                self = selfRoot,
                steps,
                remount_wake = remountWake,
                hint = "Dual-seat rollout complete (soft→soft→apply). cdp_health ops.deploy_los + version pulse."
            }, Pretty);
        }
        finally
        {
            Monitor.Exit(PublishGate);
        }
    }

    static string Tail(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        return "…" + text[^max..];
    }
}
