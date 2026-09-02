namespace CdpMcpBridge;

internal static class CdpBridgeIdentity
{
    internal static string ResolveWorkspaceKey(string? configPath)
    {
        var env = Environment.GetEnvironmentVariable("CDP_WORKSPACE_KEY");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        if (string.IsNullOrWhiteSpace(configPath))
            return "default";

        var dir = Path.GetDirectoryName(Path.GetFullPath(configPath));
        if (dir is null)
            return "default";

        var leaf = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return leaf switch
        {
            "cdp-mcp" => "cdp",
            "cdp-mcp-debug" => "cdp-debug",
            "cdp-service" => "cdp-service",
            _ => leaf.Length > 0 ? leaf : "default"
        };
    }
}
