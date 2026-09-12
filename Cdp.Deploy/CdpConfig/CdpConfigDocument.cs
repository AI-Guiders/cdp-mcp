#nullable enable

namespace Cdp.Config;

/// <summary>
/// Bootstrap sections of <c>cdp-mcp.toml</c> (ADR-0222): one DTO, role-specific views via <see cref="CdpConfigLoader"/>.
/// </summary>
public sealed class CdpConfigDocument
{
    public CdpConfigTowerSection? Tower { get; set; }
    public CdpConfigSlotsSection? Slots { get; set; }
    public CdpConfigBridgeSection? Bridge { get; set; }
    public CdpConfigToolsSection? Tools { get; set; }
    public CdpConfigForumSection? Forum { get; set; }
    public CdpConfigTenantSection? Tenant { get; set; }
    public CdpConfigDeployBootstrapSection? Deploy { get; set; }
    public CdpConfigOpsSection? Ops { get; set; }

    /// <summary>Legacy alias — maps into tower/slots/bridge when new sections are absent.</summary>
    public CdpConfigServiceLegacySection? Service { get; set; }
}

public sealed class CdpConfigTowerSection
{
    public int? ListenPort { get; set; }
    public string? Registry { get; set; }
    public int? TargetCacheMs { get; set; }
    public int? ProbeTimeoutMs { get; set; }
}

public sealed class CdpConfigSlotsSection
{
    public bool? Enabled { get; set; }
    public string? Bind { get; set; }
    public string? InstallDir { get; set; }
    public bool? AutoStart { get; set; }
    public int[]? PortRange { get; set; }
    public int? HeartbeatSeconds { get; set; }
    public string? Registry { get; set; }
}

public sealed class CdpConfigBridgeSection
{
    public string? BaseUrl { get; set; }
    public string? TokenPath { get; set; }
    public bool? AutoStartSlot { get; set; }
    public string? Composer { get; set; }
    public int? CapabilitiesPollMs { get; set; }
    public int? DeployWaitMs { get; set; }
    public int? DeployPollMs { get; set; }
    public int? DeployGapRetryMs { get; set; }
    public int? ServiceReadyMs { get; set; }
}

/// <summary>Legacy <c>[service]</c> — superseded by <c>[tower]</c>/<c>[slots]</c>/<c>[bridge]</c> (ADR-0222).</summary>
public sealed class CdpConfigServiceLegacySection
{
    public bool? Enabled { get; set; }
    public string? Bind { get; set; }
    public int? Port { get; set; }
    public string? TokenPath { get; set; }
    public string? InstallDir { get; set; }
    public bool? AutoStart { get; set; }
}
