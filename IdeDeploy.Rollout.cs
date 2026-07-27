#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeDeploy
{
    /// <summary>
    /// Soft sibling → soft self → hard sibling.
    /// From <c>cdp-debug</c>: also hard-peer onto release (live) — kills chicken-egg / hard-self brain-load.
    /// From <c>cdp</c>: returns <c>hard_self.argv</c> for terminal_* (cannot KillRunning self).
    /// </summary>
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
        var script = ResolveScript(session, Opt(args, "script"));
        if (script is null)
        {
            return Fail("rollout", selfRoot, seat, null, "script_not_found",
                $"Open cdp-mcp (or pass script= path to {ScriptName}).");
        }

        var sibling = seat switch
        {
            "cdp" => DebugTarget,
            "cdp-debug" => ReleaseTarget,
            _ => DebugTarget
        };
        var selfTarget = selfRoot ?? ReleaseTarget;
        // Survivor seat can hard-kill the other install — prefer that over terminal hard-self.
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
                seat,
                self = selfRoot,
                can_peer_hard = canPeerHard,
                steps = preview,
                hard_self = canPeerHard ? null : HardSelfHint(script, selfTarget),
                chicken_egg = canPeerHard
                    ? "Seat can hard-peer — no terminal_* needed for finish."
                    : "Old live without rollout: soft×2+hard sibling from live, then call mode=rollout from cdp-debug (peer hard).",
                hint = canPeerHard
                    ? "dry_run — soft×2 + hard sibling + hard peer (live). Drop dry_run= to execute."
                    : "dry_run — soft×2 + hard sibling. Hard-self via hard_self.argv (terminal_*) OR switch to cdp-debug and rollout."
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
                var psi = BuildPsArgs(script, mode, target, useNuGet, noNudge);
                var started = DateTime.UtcNow;
                var (exit, stdout, stderr) = RunPowerShell(psi);
                var elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                var okLine = ExtractOkLine(stdout);
                object? wake = null;
                if (exit == 0 && mode == "hard")
                {
                    try { wake = IdeIgniteArmHost.WakeAfterHardDeploy(); }
                    catch { /* best-effort */ }
                }

                steps.Add(new
                {
                    label,
                    mode,
                    target,
                    ok = exit == 0,
                    exit_code = exit,
                    elapsed_ms = elapsedMs,
                    ok_line = okLine,
                    ignite_wake = wake,
                    stdout_tail = includeRaw ? Tail(stdout, 2000) : null,
                    stderr_tail = includeRaw || exit != 0 ? Tail(stderr, includeRaw ? 1200 : 600) : null
                });

                if (exit != 0)
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "rollout",
                        pulse = $"rollout fail at {label} exit={exit}",
                        seat,
                        self = selfRoot,
                        steps,
                        hard_self = canPeerHard ? null : HardSelfHint(script, selfTarget),
                        hint = "Rollout stopped on first failure. Fix, then retry mode=rollout."
                    }, Pretty);
                }
            }
        }
        finally
        {
            Monitor.Exit(PublishGate);
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "rollout",
            pulse = canPeerHard
                ? $"rollout ok · peer-hard finished · seat={seat}"
                : $"rollout ok · hard sibling · hard_self via terminal · seat={seat}",
            seat,
            self = selfRoot,
            can_peer_hard = canPeerHard,
            steps,
            hard_self = canPeerHard ? null : HardSelfHint(script, selfTarget),
            next = canPeerHard
                ? new object[]
                {
                    new { go = "health", label = "cdp_health", why = "confirm live remount" },
                    new { go = "ignite_desk", label = "re-ARM", why = "continuity after rollout" }
                }
                : new object[]
                {
                    new { go = "terminal", label = "hard-self argv", why = "hard_self.argv via terminal_*" },
                    new { go = "deploy", label = "Or peer from debug", why = "switch cdp-debug → mode=rollout finishes live" },
                    new { go = "health", label = "cdp_health", why = "after remount" }
                },
            hint = canPeerHard
                ? "Peer hard applied — live remounts via nudge. No terminal_* needed."
                : "Hard sibling done. Finish: terminal_* hard_self.argv OR call mode=rollout from cdp-debug seat."
        }, Pretty);
    }

    static object HardSelfHint(string script, string selfTarget) => new
    {
        target = selfTarget,
        argv = new[]
        {
            "powershell.exe",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            script,
            "-Mode",
            "hard",
            "-Target",
            selfTarget
        },
        note = "Run via user-terminal terminal_run (not cdp_shell_*) — hard-self kills this seat. Prefer: hard sibling first, then mode=rollout from cdp-debug (peer hard)."
    };
}
