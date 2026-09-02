#nullable enable
using System.Reflection;

namespace CdpMcp;

internal static partial class IdeIgniteWakeLatch
{
    /// <summary>
    /// Boot/deploy hygiene: republish latch when habitat version or charge template drifted.
    /// Called from <see cref="IdeIgniteArmHost.EnsureStarted"/> — deploy alone is not enough.
    /// </summary>
    internal static WakeDoc? RefreshCanonicalIfStale(string? reason = null)
    {
        var charge = IdeIgniteChannel.ComposeArmFireCharge(IdeIgniteChannel.WakeChargePreflight.ForHabitatLatch());
        var course = IdePressureChannel.TryPeekSealedCourse();
        var current = TryRead();

        if (current is not null && !IsStale(current, charge, course))
            return current;

        var armId = current?.ArmId is { Length: > 0 } id
            ? id
            : "habitat-boot-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        return Publish(
            armId,
            charge,
            current?.Channel ?? ChannelComposer,
            reason ?? "habitat_version_refresh",
            current?.Task,
            course);
    }

    internal static bool IsStale(WakeDoc? doc, string expectedCharge, string? expectedCourse) =>
        doc is null
        || !string.Equals(doc.HabitatVersion, HabitatVersion(), StringComparison.Ordinal)
        || !string.Equals(doc.ChargeTemplateRev, IdeIgniteChannel.ChargeTemplateRev, StringComparison.Ordinal)
        || IdeIgniteChargeStaleness.ChargeMarkersStale(doc.Charge)
        || IdeIgniteChargeStaleness.CourseMarkersStale(doc.Course, expectedCourse);

    internal static string HabitatVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
