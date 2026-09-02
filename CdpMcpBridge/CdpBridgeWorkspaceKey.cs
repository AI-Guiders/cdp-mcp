using System.Security.Cryptography;
using System.Text;

namespace CdpMcpBridge;

internal static class CdpBridgeWorkspaceKey
{
    internal static string FromRoots(IEnumerable<string?>? urisOrPaths)
    {
        var paths = NormalizePaths(urisOrPaths);
        if (paths.Length == 0)
            return CdpBridgeIdentity.ResolveWorkspaceKey(null);
        return HashKey(paths);
    }

    internal static string[] NormalizePaths(IEnumerable<string?>? urisOrPaths)
    {
        if (urisOrPaths is null) return [];
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in urisOrPaths)
        {
            var path = UriToPath(raw);
            if (path is null) continue;
            try { set.Add(Path.GetFullPath(path)); }
            catch { /* skip */ }
        }

        return set.ToArray();
    }

    static string? UriToPath(string? uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return null;
        var s = uriOrPath.Trim();
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.IsFile)
                return uri.LocalPath;
            var stripped = s["file:".Length..].TrimStart('/');
            if (stripped.Length >= 2 && stripped[1] == ':')
                return stripped.Replace('/', Path.DirectorySeparatorChar);
            return null;
        }

        return s;
    }

    static string HashKey(IReadOnlyList<string> paths)
    {
        var joined = string.Join('|', paths.Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }
}
