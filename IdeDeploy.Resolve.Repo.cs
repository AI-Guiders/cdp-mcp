#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.Deploy;

namespace CdpMcp;

internal static partial class IdeDeploy
{
    /// <summary>
    /// Resolve cdp-mcp source for publish: args → session → seat [deploy].repo_root (ADR-0225).
    /// </summary>
    internal static string? ResolveRepoSearchRoot(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var key in new[] { "repo_search_root", "repo_root", "source_root" })
        {
            var raw = Opt(args, key);
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var full = Path.GetFullPath(raw.Trim());
            if (Directory.Exists(full) && CdpDeploySource.TryResolve(full) is not null)
                return full;
        }

        var searchRoot = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(searchRoot) && session.SolutionOrProjectPath is { Length: > 0 } sp)
            searchRoot = Path.GetDirectoryName(sp);
        if (!string.IsNullOrWhiteSpace(searchRoot)
            && CdpDeploySource.TryResolve(searchRoot) is not null)
            return Path.GetFullPath(searchRoot);

        return ResolveConfiguredRepoRoot();
    }

    internal static string? ResolveConfiguredRepoRoot(string? seatInstallRoot = null)
    {
        seatInstallRoot ??= ResolveSelfInstallRoot();
        foreach (var root in new[] { seatInstallRoot, ServiceTarget, ReleaseTarget })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var cfg = CdpDeploySeatConfig.ResolveSeatConfigPath(root);
            if (cfg is null)
                continue;

            try
            {
                var repo = CdpSettings.Load(cfg).Deploy.RepoRoot;
                if (string.IsNullOrWhiteSpace(repo))
                    continue;
                var full = Path.GetFullPath(repo.Trim());
                if (Directory.Exists(full) && CdpDeploySource.TryResolve(full) is not null)
                    return full;
            }
            catch
            {
                /* ignore malformed seat config */
            }
        }

        return null;
    }
}
