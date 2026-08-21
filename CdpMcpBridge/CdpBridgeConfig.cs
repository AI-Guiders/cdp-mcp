using System.Net.Http.Headers;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Serialization;

namespace CdpMcpBridge;

internal sealed class CdpBridgeSettings
{
    public required Uri BaseUrl { get; init; }
    public required string Token { get; init; }
}

internal sealed class CdpBridgeConfigLoadResult
{
    public bool IsHelp { get; init; }
    public bool IsSuccess { get; init; }
    public CdpBridgeSettings? Settings { get; init; }
    public string? ConfigPath { get; init; }
    public string? Error { get; init; }
}

internal static class CdpBridgeConfigLoader
{
    internal static CdpBridgeConfigLoadResult Load(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
            return new() { IsHelp = true };

        var configPath = ResolveConfigPath(args);
        if (configPath is null)
            return new() { IsSuccess = false, Error = "Missing --config PATH." };

        if (!File.Exists(configPath))
            return new() { IsSuccess = false, Error = $"Config not found: {configPath}" };

        var doc = TomlSerializer.Deserialize<BridgeTomlDocument>(
            File.ReadAllText(configPath),
            new TomlSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
            ?? new BridgeTomlDocument();
        var service = doc.Service;

        var bind = service?.Bind ?? "127.0.0.1";
        var port = service?.Port is > 0 and < 65536 ? service.Port.Value : 8771;
        var tokenPath = service?.TokenPath;

        var baseUrl = Environment.GetEnvironmentVariable("CDP_SERVICE_URL");
        Uri uri;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out uri!))
                return new() { IsSuccess = false, Error = $"Invalid CDP_SERVICE_URL: {baseUrl}" };
        }
        else
        {
            uri = new Uri($"http://{bind}:{port}/");
        }

        var token = Environment.GetEnvironmentVariable("CDP_SERVICE_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            var path = string.IsNullOrWhiteSpace(tokenPath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "cdp-mcp",
                    "service-token")
                : tokenPath.Trim();
            if (!File.Exists(path))
                return new() { IsSuccess = false, Error = $"Service token missing: {path}. Start CdpService first." };
            token = File.ReadAllText(path).Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
            return new() { IsSuccess = false, Error = "Empty service token." };

        return new()
        {
            IsSuccess = true,
            Settings = new CdpBridgeSettings { BaseUrl = uri, Token = token },
            ConfigPath = configPath
        };
    }

    static string? ResolveConfigPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--config" or "-c" && i + 1 < args.Length)
                return args[i + 1];
        }

        return Environment.GetEnvironmentVariable("CDP_MCP_CONFIG");
    }
}

internal sealed class BridgeTomlDocument
{
    public BridgeTomlService? Service { get; set; }
}

internal sealed class BridgeTomlService
{
    public string? Bind { get; set; }
    public int? Port { get; set; }
    public string? TokenPath { get; set; }
}

internal static class CdpBridgeHttpClient
{
    internal static HttpClient Create(
        CdpBridgeSettings settings,
        string bridgeSessionId,
        string workspaceKey,
        string? composer = null)
    {
        var http = new HttpClient { BaseAddress = settings.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-CDP-Bridge-Session", bridgeSessionId);
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-CDP-Workspace-Key", workspaceKey);
        var comp = composer
                   ?? Environment.GetEnvironmentVariable("CDP_COMPOSER")
                   ?? "main";
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-CDP-Composer", comp);
        return http;
    }
}
