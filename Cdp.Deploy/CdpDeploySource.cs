namespace Cdp.Deploy;

public sealed record CdpDeploySource(
    string RepoRoot,
    string ServiceProject,
    string BridgeProject,
    string ConfigTemplate,
    string? PreserveConfigToml)
{
    public static CdpDeploySource? TryResolve(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return null;

        var dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var serviceProject = Path.Combine(dir.FullName, "CdpMcp.csproj");
            var bridgeProject = Path.Combine(dir.FullName, "CdpMcpBridge", "CdpMcpBridge.csproj");
            if (!File.Exists(serviceProject) || !File.Exists(bridgeProject))
                continue;

            var configTemplate = Path.Combine(dir.FullName, "config", "cdp-mcp.toml");
            var preserve = Path.Combine(dir.FullName, "aid-publish.toml");
            return new CdpDeploySource(
                dir.FullName,
                serviceProject,
                bridgeProject,
                configTemplate,
                File.Exists(preserve) ? preserve : null);
        }

        return null;
    }
}
