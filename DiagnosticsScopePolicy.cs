using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Default diagnostics scope after <c>cdp_open</c>: project semantic when anchored, else syntax peek.
/// Aligns bare <c>get_diagnostics</c>, <c>cdp_buffer op=diagnostics</c>, and take verify.
/// </summary>
internal static class DiagnosticsScopePolicy
{
    public static string ResolveDefault(SessionContext session, string? filePath, string? explicitScope = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitScope))
            return explicitScope.Trim();

        if (string.IsNullOrWhiteSpace(filePath))
            return session.SolutionOrProjectPath is { Length: > 0 } ? "project" : "syntax";

        if (session.SolutionOrProjectPath is not { Length: > 0 })
            return "syntax";

        if (IsStagingPath(session, filePath))
            return "syntax";

        try
        {
            var full = Path.GetFullPath(filePath.Trim());
            if (!IsPathUnderOpenRoot(session, full))
                return "syntax";
        }
        catch
        {
            return "syntax";
        }

        return "project";
    }

    static bool IsStagingPath(SessionContext session, string filePath)
    {
        var root = session.ProjectRoot;
        if (root is not { Length: > 0 })
            return false;

        try
        {
            var full = Path.GetFullPath(filePath.Trim());
            var rel = Path.GetRelativePath(Path.GetFullPath(root), full);
            if (rel.StartsWith("..", StringComparison.Ordinal))
                return false;

            return rel.StartsWith(".cdp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || rel.StartsWith(".cdp" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(rel, ".cdp", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static bool IsPathUnderOpenRoot(SessionContext session, string fullPath)
    {
        var roots = new List<string>(capacity: 2 + session.ExtraRoots.Count);
        if (session.ProjectRoot is { Length: > 0 } primary)
            roots.Add(primary);
        foreach (var extra in session.ExtraRoots)
        {
            if (extra is { Length: > 0 })
                roots.Add(extra);
        }

        foreach (var root in roots)
        {
            try
            {
                var rootFull = Path.GetFullPath(root);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar)
                    && !rootFull.EndsWith(Path.AltDirectorySeparatorChar))
                    rootFull += Path.DirectorySeparatorChar;

                if (fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fullPath, rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // try next root
            }
        }

        var anchor = session.SolutionOrProjectPath;
        if (anchor is not { Length: > 0 })
            return false;

        try
        {
            var anchorDir = Path.GetDirectoryName(Path.GetFullPath(anchor));
            if (string.IsNullOrEmpty(anchorDir))
                return false;

            anchorDir = Path.TrimEndingDirectorySeparator(anchorDir);
            if (!anchorDir.EndsWith(Path.DirectorySeparatorChar)
                && !anchorDir.EndsWith(Path.AltDirectorySeparatorChar))
                anchorDir += Path.DirectorySeparatorChar;

            return fullPath.StartsWith(anchorDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
