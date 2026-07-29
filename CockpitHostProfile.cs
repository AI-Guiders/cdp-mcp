#nullable enable
namespace CdpMcp;

/// <summary>Shared host pulse for <c>cdp_cockpit_host</c> and <c>cdp_icm</c> scenes.</summary>
internal static class CockpitHostProfile
{
    public readonly record struct Snapshot(string GuiHost, string HostProfile, int? Pid);

    public static Snapshot Current() => IdeCockpitHostChannel.GetHostPulse();
}
