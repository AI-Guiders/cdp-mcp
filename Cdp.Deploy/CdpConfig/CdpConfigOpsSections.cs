#nullable enable

namespace Cdp.Config;

/// <summary>ADR-0224 wave D: operator paths and tuning from TOML — not env.</summary>
public sealed class CdpConfigToolsSection
{
    public string? Rg { get; set; }
    public string? PluginsRoot { get; set; }
    public string? OpenvsxBase { get; set; }
    public string? OpencodeBin { get; set; }
    public string? OpencodeDirectory { get; set; }
}

public sealed class CdpConfigForumSection
{
    public string? Root { get; set; }
}

public sealed class CdpConfigTenantSection
{
    public int? IdleTtlMinutes { get; set; }
}

public sealed class CdpConfigDeployBootstrapSection
{
    public string? Script { get; set; }
    public string? RepoRoot { get; set; }
}

public sealed class CdpConfigOpsSection
{
    public bool? ExploreCorrEnabled { get; set; }
    public bool? ShellIgniteArm { get; set; }
    public bool? LifecycleIgniteArm { get; set; }
    public bool? ToolWakeArm { get; set; }
    public bool? OomWakeCdtEdge { get; set; }
}
