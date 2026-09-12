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
    static readonly (string Env, string TomlHint)[] ForbiddenEnvOverrides =
    [
        ("CDP_SERVICE_URL", "[bridge].base_url"),
        ("CDP_SERVICE_TOKEN", "[bridge].token_path"),
        ("CDP_SERVICE_INSTALL_DIR", "[slots].install_dir"),
        ("CDP_SERVICE_AUTO_START", "[slots].auto_start / [bridge].auto_start_slot"),
    ];

    internal static CdpBridgeConfigLoadResult Load(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
            return new() { IsHelp = true };

        foreach (var (env, hint) in ForbiddenEnvOverrides)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(env)))
                continue;

            return new()
            {
                IsSuccess = false,
                Error = $"{env} is forbidden (ADR-0222); use {hint} in cdp-mcp.toml."
            };
        }

        var configPath = ResolveConfigPath(args) ?? CdpConfigPaths.DefaultBridgeConfigPath;
        if (!File.Exists(configPath))
            return new() { IsSuccess = false, Error = $"Config not found: {configPath}" };

        var doc = CdpConfigLoader.Load(configPath);
        var bridge = CdpConfigLoader.MapBridge(doc);
        var resolvedTokenPath = bridge.TokenPath ?? CdpConfigLoader.DefaultTokenPath();

        if (!File.Exists(resolvedTokenPath))
            return new() { IsSuccess = false, Error = $"Service token missing: {resolvedTokenPath}. Start CdpService first." };

        var token = File.ReadAllText(resolvedTokenPath).Trim();
        if (string.IsNullOrWhiteSpace(token))
            return new() { IsSuccess = false, Error = "Empty service token." };

        var installDir = string.IsNullOrWhiteSpace(bridge.InstallDir)
            ? null
            : Path.GetFullPath(bridge.InstallDir.Trim());

        return new()
        {
            IsSuccess = true,
            Settings = new CdpBridgeSettings
            {
                BaseUrl = bridge.BaseUrl,
                Token = token,
                TokenPath = resolvedTokenPath,
                InstallDir = installDir,
                ServiceConfigPath = configPath,
                AutoStart = bridge.AutoStart
            },
            ConfigPath = configPath
        };
    }

    static string? ResolveConfigPath(string[] args) =>
        ConfigPathResolver.TryResolve(args, environmentVariable: null);
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
