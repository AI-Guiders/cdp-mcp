using AIGuiders.Cli;
using Cdp.Config;
using System.Net.Http.Headers;
using System.Text.Json;

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

        var doc = CdpConfigLoader.Load(configPath);
        var bridge = CdpConfigLoader.MapBridge(doc);
        var resolvedTokenPath = bridge.TokenPath ?? CdpConfigLoader.DefaultTokenPath();

        var baseUrl = Environment.GetEnvironmentVariable("CDP_SERVICE_URL");
        Uri uri;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            WarnDeprecatedEnv("CDP_SERVICE_URL", "[bridge].base_url");
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out uri!))
                return new() { IsSuccess = false, Error = $"Invalid CDP_SERVICE_URL: {baseUrl}" };
        }
        else
        {
            uri = bridge.BaseUrl;
        }

        var token = Environment.GetEnvironmentVariable("CDP_SERVICE_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            if (!File.Exists(resolvedTokenPath))
                return new() { IsSuccess = false, Error = $"Service token missing: {resolvedTokenPath}. Start CdpService first." };
            token = File.ReadAllText(resolvedTokenPath).Trim();
        }
        else
        {
            WarnDeprecatedEnv("CDP_SERVICE_TOKEN", "[bridge].token_path");
        }

        if (string.IsNullOrWhiteSpace(token))
            return new() { IsSuccess = false, Error = "Empty service token." };

        var installDir = Environment.GetEnvironmentVariable("CDP_SERVICE_INSTALL_DIR");
        if (string.IsNullOrWhiteSpace(installDir))
            installDir = bridge.InstallDir;
        else
            WarnDeprecatedEnv("CDP_SERVICE_INSTALL_DIR", "[slots].install_dir");
        installDir = string.IsNullOrWhiteSpace(installDir) ? null : Path.GetFullPath(installDir.Trim());

        var autoStart = bridge.AutoStart;
        if (Environment.GetEnvironmentVariable("CDP_SERVICE_AUTO_START") is { Length: > 0 } autoRaw
            && bool.TryParse(autoRaw, out var autoEnv))
        {
            WarnDeprecatedEnv("CDP_SERVICE_AUTO_START", "[slots].auto_start / [bridge].auto_start_slot");
            autoStart = autoEnv;
        }

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
                AutoStart = autoStart
            },
            ConfigPath = configPath
        };
    }

    static void WarnDeprecatedEnv(string envName, string tomlHint) =>
        Console.Error.WriteLine(
            $"WARNING: {envName} is deprecated (ADR-0222); use {tomlHint} in cdp-mcp.toml.");

    static string? ResolveConfigPath(string[] args) =>
        ConfigPathResolver.TryResolve(args, "CDP_MCP_CONFIG");
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
