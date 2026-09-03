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
        var peerHard = IsTruthy(args, "peer_hard") || IsTruthy(args, "finish_peer");

        var selfRoot = ResolveSelfInstallRoot();
        var seat = ClassifySeat(selfRoot);
        var sibling = CdpDeployLayout.Default.SiblingBridgeForSeat(seat);
        var selfTarget = selfRoot ?? ReleaseTarget;
        var canPeerHard = seat is "cdp-debug" || peerHard;
        var peerTarget = seat == "cdp-debug" ? ReleaseTarget : DebugTarget;

        if (dryRun)
        {
            var preview = new List<object>
            {
                new { mode = "soft", target = sibling },
                new { mode = "soft", target = selfTarget },
                new { mode = "hard", target = sibling }
            };
            if (canPeerHard)
                preview.Add(new { mode = "hard", target = peerTarget, label = "hard_peer" });

            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "rollout",
                dry_run = true,
                engine = "cdp.deploy/csharp",
                seat,
                self = selfRoot,
                steps = preview
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
                ("hard", sibling, "hard_sibling")
            };
            if (canPeerHard)
                plan.Add(("hard", peerTarget, "hard_peer"));

            foreach (var (mode, target, label) in plan)
            {
                var started = DateTime.UtcNow;
                var resolved = ResolveTarget(selfRoot, seat, target, mode, force: false);
                var planResult = BuildPlan(session, mode, selfRoot, resolved, useNuGet, noNudge, force: false);
                CdpDeployStepResult step;
                var exit = 0;
                string? stderr = null;
                try
                {
                    if (!planResult.Ok || planResult.Plan is null)
                        throw new InvalidOperationException(planResult.Hint ?? planResult.Error ?? "plan failed");
                    step = CdpDeployOrchestrator.Run(planResult.Plan);
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
                        hint = "Rollout stopped on first failure. Fix, then retry mode=rollout."
                    }, Pretty);
                }
            }

            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "rollout",
                engine = "cdp.deploy/csharp",
                pulse = "rollout ok",
                seat,
                self = selfRoot,
                steps
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
