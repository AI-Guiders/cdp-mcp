#nullable enable

namespace CdpMcp;

internal static partial class IdePlanPromote
{
    public static string ResolveInbox(string? projectRoot, string? dirOverride)
    {
        if (!string.IsNullOrWhiteSpace(dirOverride))
            return Path.GetFullPath(dirOverride);
        if (!string.IsNullOrWhiteSpace(projectRoot))
            return Path.GetFullPath(Path.Combine(projectRoot, ".cdp", "plans"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "plans");
    }
}
