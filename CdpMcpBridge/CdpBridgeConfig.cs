using AIGuiders.Cli;
using System.Net.Http.Headers;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Serialization;

namespace CdpMcpBridge;

internal sealed class CdpBridgeSettings
{
    public required Uri BaseUrl { get; init; }
    public required string Token { get; init; }
    public string? TokenPath { get; init; }
    /// <summary>Durable seat root (e.g. D:\cdp-service) for bridge auto-start.</summary>
    public string? InstallDir { get; init; }
    /// <summary>Bridge --config path; passed to service on auto-start when present.</summary>
    public string? ServiceConfigPath { get; init; }
    /// <summary>When true and <see cref="InstallDir"/> set, probe /healthz and spawn sidecar on connection refused.</summary>
    public bool AutoStart { get; init; }
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

        var resolvedTokenPath = string.IsNullOrWhiteSpace(tokenPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cdp-mcp",
                "service-token")
            : tokenPath.Trim();

        var token = Environment.GetEnvironmentVariable("CDP_SERVICE_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            if (!File.Exists(resolvedTokenPath))
                return new() { IsSuccess = false, Error = $"Service token missing: {resolvedTokenPath}. Start CdpService first." };
            token = File.ReadAllText(resolvedTokenPath).Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
            return new() { IsSuccess = false, Error = "Empty service token." };

        var installDir = Environment.GetEnvironmentVariable("CDP_SERVICE_INSTALL_DIR");
        if (string.IsNullOrWhiteSpace(installDir))
            installDir = service?.InstallDir;
        installDir = string.IsNullOrWhiteSpace(installDir) ? null : Path.GetFullPath(installDir.Trim());

        var autoStart = service?.AutoStart;
        if (Environment.GetEnvironmentVariable("CDP_SERVICE_AUTO_START") is { Length: > 0 } autoRaw
            && bool.TryParse(autoRaw, out var autoEnv))
            autoStart = autoEnv;
        else if (autoStart is null)
            autoStart = !string.IsNullOrWhiteSpace(installDir);

        return new()
        {
            IsSuccess = true,
            Settings = new CdpBridgeSettings
            {
                BaseUrl = uri,
                Token = token,
                TokenPath = resolvedTokenPath,
                InstallDir = installDir,
                ServiceConfigPath = configPath,
                AutoStart = autoStart.Value
            },
            ConfigPath = configPath
        };
    }

    static string? ResolveConfigPath(string[] args) =>
        ConfigPathResolver.TryResolve(args, "CDP_MCP_CONFIG");
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
    public string? InstallDir { get; set; }
    public bool? AutoStart { get; set; }
}

internal static class CdpBridgeHttpClient
{
    internal static HttpClient Create(CdpBridgeSettings settings, CdpBridgeTenantHeadersState tenantState)
    {
        var handler = new CdpBridgeTenantHeadersHandler(
            tenantState,
            settings.BaseUrl,
            settings.Token,
            settings.TokenPath)
        {
            InnerHandler = new HttpClientHandler()
        };
        return new HttpClient(handler) { BaseAddress = settings.BaseUrl };
    }
}
