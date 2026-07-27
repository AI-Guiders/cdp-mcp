#nullable enable
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Dual-instance Deploy — thin wrapper over <c>publish-and-deploy.ps1</c>.
/// Hard defaults to the sibling install path so KillRunning does not target self.
/// Partials: Resolve (target/script/seat), Execute (payloads).
/// </summary>
internal static partial class IdeDeploy
{
    public const string Schema = "deploy/v0";
    public const string ReleaseTarget = @"D:\cdp-mcp";
    public const string DebugTarget = @"D:\cdp-mcp-debug";
    public const string ScriptName = "publish-and-deploy.ps1";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly object PublishGate = new();

    public static string Run(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var mode = NormalizeMode(Opt(args, "mode") ?? "hard");
        var dryRun = IsTruthy(args, "dry_run") || IsTruthy(args, "peek");
        var force = IsTruthy(args, "force");
        var useNuGet = IsTruthy(args, "use_nuget") || IsTruthy(args, "UseNuGet");
        var noNudge = IsTruthy(args, "no_nudge") || IsTruthy(args, "NoNudgeMcp");

        if (mode == "rollout")
            return Rollout(session, args);

        var selfRoot = ResolveSelfInstallRoot();
        var seat = ClassifySeat(selfRoot);
        var resolved = ResolveTarget(selfRoot, seat, Opt(args, "target") ?? Opt(args, "to"), mode, force);
        if (!resolved.Ok)
            return Fail(mode, selfRoot, seat, resolved.Target, resolved.Error!, resolved.Hint);

        var script = ResolveScript(session, Opt(args, "script"));
        if (script is null)
        {
            return Fail(mode, selfRoot, seat, resolved.Target, "script_not_found",
                $"Open cdp-mcp (or pass script= path to {ScriptName}). Sticky warm restores last desk — then retry go=deploy.");
        }

        var psiArgs = BuildPsArgs(script, mode, resolved.Target!, useNuGet, noNudge);
        if (dryRun)
            return DryRunPayload(mode, selfRoot, seat, resolved, script, psiArgs);

        if (!Monitor.TryEnter(PublishGate))
        {
            return Fail(mode, selfRoot, seat, resolved.Target, "deploy_in_flight",
                "Another cdp_deploy is still publishing — wait for it, then retry (soft/hard sequential).");
        }

        try
        {
            return Execute(mode, selfRoot, seat, resolved, script, psiArgs, args);
        }
        finally
        {
            Monitor.Exit(PublishGate);
        }
    }

    static string? ExtractOkLine(string stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return null;
        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var t = line.Trim();
            if (t.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("HARD deployed:", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("SOFT staged:", StringComparison.OrdinalIgnoreCase))
                return Tail(t, 160);
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
