#nullable enable

namespace CdpMcp;

/// <summary>
/// Cursor guest-host OOM ("Window terminated unexpectedly (reason: 'oom')") —
/// tooth (native New Window) + AutoIgnition wake after CDT recovers.
/// </summary>
internal static class IdeOomWake
{
    public const string ArmIdPrefix = "oom-wake-";
    public const string ArmTask = "cursor-oom-recovered";
    public const string ChargeMode = "oom";
    /// <summary>Machine-readable wake reason for agent (composer + arm SSOT).</summary>
    public const string Reason = "oom";

    /// <summary>Settle after CDT returns before Composer inject.</summary>
    public static int DefaultDueSeconds { get; set; } = 8;

    /// <summary>CDT must stay down at least this long before Up edge counts as OOM recovery.</summary>
    public static TimeSpan MinDownDuration { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Do not schedule another OOM wake sooner than this.</summary>
    public static TimeSpan WakeCooldown { get; set; } = TimeSpan.FromSeconds(60);
}
