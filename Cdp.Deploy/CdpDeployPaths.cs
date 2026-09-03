namespace Cdp.Deploy;

internal static class CdpDeployPaths
{
    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        return string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path) => Path.GetFullPath(path);

    public static string ResolveLiveFromStaged(string stagedRoot, string defaultLive)
    {
        if (string.IsNullOrWhiteSpace(stagedRoot))
            return defaultLive;

        return stagedRoot.EndsWith(".next", StringComparison.OrdinalIgnoreCase)
            ? stagedRoot[..^5]
            : defaultLive;
    }
}
