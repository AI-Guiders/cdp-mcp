#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Cdp.Core;
using DotnetDebugMcp;
using GitMcp.Core;

namespace CdpMcp;

internal static partial class IdeBuildSaChannel
{
    static Snap Capture(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var targetOk = IdeSessionLifecycle.TryResolveTarget(session, args, out var target, out _);
        var scm = session.ScmRoot ?? session.ProjectRoot;
        var dirty = scm is { Length: > 0 }
            ? IdeReviewChannel.ListDirtyFiles(scm)
            : Array.Empty<IdeReviewChannel.FileCard>();
        var secretHits = dirty.Count(d => d.Risk.Equals("secret", StringComparison.OrdinalIgnoreCase));

        string? branch = null;
        int? ahead = null;
        int? behind = null;
        if (scm is { Length: > 0 })
        {
            branch = RunGit(scm, "rev-parse --abbrev-ref HEAD")?.Trim();
            var lr = RunGit(scm, "rev-list --left-right --count @{u}...HEAD");
            if (lr is { Length: > 0 })
            {
                var parsed = GitScene.ParseLeftRightCount(lr);
                if (parsed is { } ab)
                {
                    // left = upstream ahead of us (behind), right = we ahead of upstream
                    // git rev-list --left-right --count @{u}...HEAD → behind\tahead typically
                    // ParseLeftRightCount returns (Ahead, Behind) as (left, right) from docs:
                    // "ahead (left), behind (right)" for A...B — with @{u}...HEAD left=commits on upstream not in HEAD = behind us
                    behind = ab.Ahead;
                    ahead = ab.Behind;
                }
            }
        }

        return new Snap(
            targetOk ? target : null,
            targetOk,
            scm,
            branch,
            DebugSession.CurrentClient is not null,
            DebugSession.LastStoppedThreadId > 0,
            dirty,
            secretHits,
            ahead,
            behind);
    }

    static string? RunGit(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            return p.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }

}
