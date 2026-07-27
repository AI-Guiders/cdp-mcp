#nullable enable
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeDeploy
{
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
}
