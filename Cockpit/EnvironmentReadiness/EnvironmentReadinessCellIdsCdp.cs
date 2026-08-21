#nullable enable

namespace CdpMcp.Cockpit.EnvironmentReadiness;

/// <summary>CDP/Glass ER cell ids beyond CIDE deck (ADR-0002 CDP extensions).</summary>
public static class EnvironmentReadinessCellIdsCdp
{
    public const string CdpSection = "environment_cdp_section";
    public const string CdpService = "environment_cdp_service";
    public const string CdpBackends = "environment_cdp_backends";
    public const string CdpSeat = "environment_cdp_seat";
    public const string FreshnessCache = "environment_cdp_freshness";
    public const string MemoryBackends = "environment_cdp_memory";
}
