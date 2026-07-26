#nullable enable
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Dual-instance Deploy — thin wrapper over <c>publish-and-deploy.ps1</c>.
/// Hard defaults to the sibling install path so KillRunning does not target self.
/// </summary>
internal static class IdeDeploy
{
    public const string Schema = "deploy/v0";
    public const string ReleaseTarget = @"D:\cdp-mcp";
    public const string DebugTarget = @"D:\cdp-mcp-debug";
    public const string ScriptName = "publish-and-deploy.ps1";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Run(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var mode = NormalizeMode(Opt(args, "mode") ?? "hard");
        var dryRun = IsTruthy(args, "dry_run") || IsTruthy(args, "peek");
        var force = IsTruthy(args, "force");
        var useNuGet = IsTruthy(args, "use_nuget") || IsTruthy(args, "UseNuGet");
        var noNudge = IsTruthy(args, "no_nudge") || IsTruthy(args, "NoNudgeMcp");

        var selfRoot = ResolveSelfInstallRoot();
        var seat = ClassifySeat(selfRoot);
        var targetRaw = Opt(args, "target") ?? Opt(args, "to");
        var resolved = ResolveTarget(selfRoot, seat, targetRaw, mode, force);
        if (!resolved.Ok)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "deploy",
                error = resolved.Error,
                mode,
                self = selfRoot,
                seat,
                target = resolved.Target,
                hint = resolved.Hint
            }, Pretty);
        }

        var script = ResolveScript(session, Opt(args, "script"));
        if (script is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "deploy",
                error = "script_not_found",
                mode,
                self = selfRoot,
                seat,
                target = resolved.Target,
                hint =
                    $"Open cdp-mcp (or pass script= path to {ScriptName}). " +
                    "Sticky warm restores last desk — then retry go=deploy."
            }, Pretty);
        }

        var psiArgs = BuildPsArgs(script, mode, resolved.Target!, useNuGet, noNudge);
        if (dryRun)
        {
            return JsonSerializer.Serialize(new
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

        var started = DateTime.UtcNow;
        var (exit, stdout, stderr) = RunPowerShell(psiArgs);
        var elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = exit == 0,
            op = "deploy",
            mode,
            self = selfRoot,
            seat,
            target = resolved.Target,
            sibling = resolved.Sibling,
            script,
            exit_code = exit,
            elapsed_ms = elapsedMs,
            stdout_tail = Tail(stdout, 4000),
            stderr_tail = Tail(stderr, 2000),
            next = exit == 0
                ? new object[]
                {
                    new { go = "health", label = "cdp_health", why = "confirm version after remount" },
                    new { go = "cockpit", label = "Desk", why = "reorient after deploy" }
                }
                : null,
            hint = exit == 0
                ? (mode == "hard"
                    ? "Hard deploy done. Sibling remounts via CDP_RELOAD_NUDGE; stay on survivor or switch back."
                    : "Soft staged (.next + pending_update). Apply with mode=hard when ready.")
                : "Deploy failed — see stderr_tail / exit_code."
        }, Pretty);
    }

    internal static string? ResolveSelfInstallRoot()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(Path.GetFullPath(exe));
    }

    internal static string ClassifySeat(string? selfRoot)
    {
        if (SamePath(selfRoot, ReleaseTarget))
            return "cdp";
        if (SamePath(selfRoot, DebugTarget))
            return "cdp-debug";
        return "other";
    }

    internal readonly record struct TargetDecision(
        bool Ok,
        string? Target,
        string? Sibling,
        string? Error,
        string? Hint);

    internal static TargetDecision ResolveTarget(
        string? selfRoot,
        string seat,
        string? targetRaw,
        string mode,
        bool force)
    {
        var sibling = seat switch
        {
            "cdp" => DebugTarget,
            "cdp-debug" => ReleaseTarget,
            _ => ReleaseTarget
        };

        string target;
        var raw = (targetRaw ?? "").Trim();
        if (raw.Length == 0 || raw.Equals("sibling", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            target = sibling;
        }
        else if (raw.Equals("self", StringComparison.OrdinalIgnoreCase)
                 || raw.Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            target = selfRoot ?? ReleaseTarget;
        }
        else if (raw.Equals("release", StringComparison.OrdinalIgnoreCase)
                 || raw.Equals("cdp", StringComparison.OrdinalIgnoreCase))
        {
            target = ReleaseTarget;
        }
        else if (raw.Equals("debug", StringComparison.OrdinalIgnoreCase)
                 || raw.Equals("cdp-debug", StringComparison.OrdinalIgnoreCase))
        {
            target = DebugTarget;
        }
        else
        {
            target = Path.GetFullPath(raw);
        }

        if (mode == "hard" && SamePath(target, selfRoot) && !force)
        {
            return new TargetDecision(
                false,
                target,
                sibling,
                "refuse_hard_self",
                "Hard KillRunning cannot reliably kill this process from inside. " +
                "Default: target=sibling (or switch seats). force=true to override.");
        }

        return new TargetDecision(true, target, sibling, null, null);
    }

    internal static string? ResolveScript(SessionContext session, string? explicitPath)
    {
        if (explicitPath is { Length: > 0 })
        {
            var p = Path.GetFullPath(explicitPath);
            return File.Exists(p) ? p : null;
        }

        var env = Environment.GetEnvironmentVariable("CDP_DEPLOY_SCRIPT");
        if (env is { Length: > 0 } && File.Exists(env))
            return Path.GetFullPath(env);

        foreach (var root in CandidateRoots(session))
        {
            var hit = FindScriptUp(root);
            if (hit is not null)
                return hit;
        }

        return null;
    }

    static IEnumerable<string> CandidateRoots(SessionContext session)
    {
        if (session.ProjectRoot is { Length: > 0 } pr)
            yield return pr;
        if (session.SolutionOrProjectPath is { Length: > 0 } sp)
        {
            var dir = Path.GetDirectoryName(sp);
            if (dir is { Length: > 0 })
                yield return dir;
        }

        // Common monorepo layout: …/open/cdp-mcp next to lab / other projects.
        if (session.ProjectRoot is { Length: > 0 } root)
        {
            var open = Directory.GetParent(root)?.FullName;
            if (open is not null)
                yield return Path.Combine(open, "cdp-mcp");
        }
    }

    static string? FindScriptUp(string start)
    {
        try
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var script = Path.Combine(dir.FullName, ScriptName);
                var csproj = Path.Combine(dir.FullName, "CdpMcp.csproj");
                if (File.Exists(script) && File.Exists(csproj))
                    return script;
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    static string BuildPsArgs(string script, string mode, string target, bool useNuGet, bool noNudge)
    {
        var sb = new StringBuilder();
        sb.Append("-NoProfile -ExecutionPolicy Bypass -File ");
        sb.Append(Quote(script));
        sb.Append(" -Mode ").Append(mode);
        sb.Append(" -Target ").Append(Quote(target));
        if (useNuGet)
            sb.Append(" -UseNuGet");
        if (noNudge)
            sb.Append(" -NoNudgeMcp");
        return sb.ToString();
    }

    static (int Exit, string Stdout, string Stderr) RunPowerShell(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start powershell.exe");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }

    static string NormalizeMode(string mode)
    {
        var m = mode.Trim().ToLowerInvariant();
        return m is "soft" or "hard" ? m : "hard";
    }

    static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        return string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }

    static string Quote(string path) => "\"" + path.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    static string Tail(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        return "…" + text[^max..];
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
        => args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static bool IsTruthy(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b
                                   || string.Equals(el.GetString(), "1", StringComparison.Ordinal),
            _ => false
        };
    }
}
