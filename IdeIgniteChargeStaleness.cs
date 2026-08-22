#nullable enable

namespace CdpMcp;

/// <summary>
/// Detect stale ignite wake charge/course without diffing full Composer text.
/// </summary>
internal static class IdeIgniteChargeStaleness
{
    internal static readonly string[] ForbiddenChargeMarkers =
    [
        "do not rewrite for agent convenience",
        "Drive Glass Done",
    ];

    internal static readonly string[] RequiredChargeMarkers =
    [
        "joint course",
        "твоё — твоё",
    ];

    internal static bool ChargeMarkersStale(string? charge)
    {
        if (string.IsNullOrWhiteSpace(charge))
            return true;

        foreach (var bad in ForbiddenChargeMarkers)
        {
            if (charge.Contains(bad, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var req in RequiredChargeMarkers)
        {
            if (!charge.Contains(req, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool CourseMarkersStale(string? latchedCourse, string? expectedCourse)
    {
        if (string.IsNullOrWhiteSpace(latchedCourse))
            return expectedCourse is { Length: > 0 };

        if (latchedCourse.Contains("DEFERRED", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var raw in latchedCourse.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (IdePressureChannel.IsLegacyGlassFirstPriorityLine(raw.Trim()))
                    return true;
            }
        }

        if (string.IsNullOrWhiteSpace(expectedCourse))
            return false;

        var normalizedLatched = NormalizeCourse(latchedCourse);
        var normalizedExpected = NormalizeCourse(expectedCourse);
        return !string.Equals(normalizedLatched, normalizedExpected, StringComparison.Ordinal);
    }

    static string NormalizeCourse(string course) =>
        IdePressureChannel.PurgeLegacyGlassFirstLines(
            IdePressureChannel.EnsureCourseCriteria(course) ?? course).Trim();
}
