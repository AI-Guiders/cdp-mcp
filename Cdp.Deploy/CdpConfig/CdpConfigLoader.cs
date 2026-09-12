#nullable enable
using System.Text.Json;
using Tomlyn;
using Tomlyn.Serialization;

namespace Cdp.Config;

public enum CdpConfigRole
{
    Tower,
    Slot,
    Bridge
}

public sealed class CdpTowerRoleConfig
{
    public const int DefaultListenPort = 8771;
    public const string DefaultBind = "127.0.0.1";

    public int ListenPort { get; init; } = DefaultListenPort;
}

public sealed class CdpSlotRoleConfig
{
    public bool Enabled { get; init; } = true;
    public string Bind { get; init; } = CdpTowerRoleConfig.DefaultBind;
    /// <summary>0 = dynamic slot port (<see cref="CdpSlotRegistry.PickFreePort"/>); never read from TOML.</summary>
    public int Port { get; init; }
    public string? TokenPath { get; init; }
}

public sealed class CdpBridgeRoleConfig
{
    public Uri BaseUrl { get; init; } = new($"http://{CdpTowerRoleConfig.DefaultBind}:{CdpTowerRoleConfig.DefaultListenPort}/");
    public string? TokenPath { get; init; }
    public string? InstallDir { get; init; }
    public bool AutoStart { get; init; }
}

public static class CdpConfigLoader
{
    public static readonly TomlSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static CdpConfigDocument Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new CdpConfigDocument();

        return Parse(File.ReadAllText(path));
    }

    public static CdpConfigDocument Parse(string toml) =>
        TomlSerializer.Deserialize<CdpConfigDocument>(toml, SerializerOptions) ?? new();

    public static T MapForRole<T>(CdpConfigDocument doc, CdpConfigRole role) =>
        role switch
        {
            CdpConfigRole.Tower when typeof(T) == typeof(CdpTowerRoleConfig) => (T)(object)MapTower(doc),
            CdpConfigRole.Slot when typeof(T) == typeof(CdpSlotRoleConfig) => (T)(object)MapSlot(doc),
            CdpConfigRole.Bridge when typeof(T) == typeof(CdpBridgeRoleConfig) => (T)(object)MapBridge(doc),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, $"Unsupported MapForRole pair: {role}/{typeof(T).Name}")
        };

    public static CdpTowerRoleConfig MapTower(CdpConfigDocument doc) =>
        new() { ListenPort = ResolveListenPort(doc) };

    public static CdpSlotRoleConfig MapSlot(CdpConfigDocument doc)
    {
        var service = doc.Service;
        return new CdpSlotRoleConfig
        {
            Enabled = service?.Enabled ?? true,
            Bind = ResolveBind(doc),
            Port = 0,
            TokenPath = NormalizeOptionalPath(service?.TokenPath)
        };
    }

    public static CdpBridgeRoleConfig MapBridge(CdpConfigDocument doc)
    {
        var installDir = ResolveInstallDir(doc);
        return new CdpBridgeRoleConfig
        {
            BaseUrl = ResolveBaseUrl(doc),
            TokenPath = ResolveTokenPath(doc),
            InstallDir = installDir,
            AutoStart = ResolveBridgeAutoStart(doc, installDir)
        };
    }

    public static string DefaultTokenPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "service-token");

    public static string ResolveTokenPath(CdpConfigDocument doc)
    {
        var configured = NormalizeOptionalPath(doc.Bridge?.TokenPath)
            ?? NormalizeOptionalPath(doc.Service?.TokenPath);
        return configured ?? DefaultTokenPath();
    }

    static int ResolveListenPort(CdpConfigDocument doc)
    {
        if (doc.Tower?.ListenPort is > 0 and < 65536)
            return doc.Tower.ListenPort.Value;

        if (doc.Service?.Port is > 0 and < 65536)
            return doc.Service.Port.Value;

        return CdpTowerRoleConfig.DefaultListenPort;
    }

    static string ResolveBind(CdpConfigDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.Service?.Bind))
            return doc.Service.Bind.Trim();

        return CdpTowerRoleConfig.DefaultBind;
    }

    static Uri ResolveBaseUrl(CdpConfigDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.Bridge?.BaseUrl)
            && Uri.TryCreate(doc.Bridge.BaseUrl.Trim(), UriKind.Absolute, out var explicitUrl))
            return explicitUrl;

        var bind = ResolveBind(doc);
        var port = ResolveListenPort(doc);
        return new Uri($"http://{bind}:{port}/");
    }

    static string? ResolveInstallDir(CdpConfigDocument doc)
    {
        var raw = doc.Slots?.InstallDir ?? doc.Service?.InstallDir;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return Path.GetFullPath(raw.Trim());
    }

    static bool ResolveBridgeAutoStart(CdpConfigDocument doc, string? installDir)
    {
        if (doc.Bridge?.AutoStartSlot is { } bridgeAuto)
            return bridgeAuto;

        if (doc.Slots?.AutoStart is { } slotsAuto)
            return slotsAuto;

        if (doc.Service?.AutoStart is { } legacyAuto)
            return legacyAuto;

        return !string.IsNullOrWhiteSpace(installDir);
    }

    static string? NormalizeOptionalPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();
}
