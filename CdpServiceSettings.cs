namespace CdpMcp;

internal sealed class CdpServiceSettings
{
    public bool Enabled { get; init; } = true;
    public string Bind { get; init; } = "127.0.0.1";
    public int Port { get; init; }
    public string? TokenPath { get; init; }

    public Uri BaseUri =>
        Port > 0
            ? new($"http://{Bind}:{Port}/")
            : new($"http://{Bind}:{CdpSlotRegistry.FirstSlotPort}/");

    public string ResolveTokenPath()
    {
        if (!string.IsNullOrWhiteSpace(TokenPath))
            return TokenPath.Trim();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "service-token");
    }
}
