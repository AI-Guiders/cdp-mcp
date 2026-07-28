#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeDeploy
{
    static string Execute(
        string mode,
        string? selfRoot,
        string seat,
        TargetDecision resolved,
        string script,
        string psiArgs,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var started = DateTime.UtcNow;
        var (exit, stdout, stderr) = RunPowerShell(psiArgs);
        var elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
        var includeRaw = IsTruthy(args, "include_raw") || IsTruthy(args, "include_raw_output");
        var okLine = ExtractOkLine(stdout);

        object? igniteWake = null;
        object? remountWake = null;
        if (exit == 0 && mode == "hard")
        {
            try { IdeRemountWake.MarkPending(resolved.Target!, "hard_deploy"); }
            catch { /* best-effort — ps1 also writes pending */ }
            try { igniteWake = IdeIgniteArmHost.WakeAfterHardDeploy(); }
            catch { /* best-effort — deploy already succeeded */ }
            var targetSeat = ClassifySeat(resolved.Target);
            remountWake = new
            {
                pending_seat = targetSeat,
                pending_path = IdeRemountWake.PendingPathForSeat(targetSeat),
                hint = "Target MCP boot consumes pending → Autoi 'MCP remounted / initialized'"
            };
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = exit == 0,
            op = "deploy",
            pulse = exit == 0
                ? $"deploy {mode} ok → {resolved.Target}" + (okLine is null ? "" : $" · {okLine}")
                : $"deploy {mode} fail exit={exit}",
            mode,
            self = selfRoot,
            seat,
            target = resolved.Target,
            sibling = resolved.Sibling,
            script,
            exit_code = exit,
            elapsed_ms = elapsedMs,
            ignite_wake = igniteWake,
            remount_wake = remountWake,
            stdout_tail = includeRaw ? Tail(stdout, 4000) : null,
            stderr_tail = includeRaw || exit != 0 ? Tail(stderr, includeRaw ? 2000 : 800) : null,
            next = exit == 0
                ? new object[]
                {
                    new { go = "health", label = "cdp_health", why = "confirm version after remount" },
                    new { go = "ignite_desk", label = "Ignite arms", why = "survivor reclaim; target remount Autoi initialized" },
                    new { go = "cockpit", label = "Desk", why = "reorient after deploy" }
                }
                : null,
            hint = exit == 0
                ? (mode == "hard"
                    ? (includeRaw
                        ? "Hard deploy done. Remount-wake pending armed for target; survivor reclaimed overdue Autoi arms."
                        : "Hard deploy done. Target remount Autoi will say initialized; survivor reclaims overdue arms.")
                    : "Soft staged (.next + pending_update). Apply with mode=hard when ready.")
                : "Deploy failed — see stderr_tail / exit_code. include_raw=true for full tails."
        }, Pretty);
    }

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
        string script,
        string psiArgs) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "deploy",
            dry_run = true,
            mode,
            self = selfRoot,
            seat,
            target = resolved.Target,
            sibling = resolved.Sibling,
            script,
            argv = psiArgs,
            hint = "dry_run — no process started. Drop dry_run= to execute."
        }, Pretty);
}
