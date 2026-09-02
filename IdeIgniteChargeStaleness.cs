#nullable enable
using CdpMcp.Habitat;

namespace CdpMcp;

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

    static readonly IRule<string?, bool>[] ChargeStaleRules =
    [
        new ChargeEmptyStaleRule(),
        new ChargeForbiddenMarkerStaleRule(),
        new ChargeRequiredMarkerStaleRule(),
    ];

    static readonly IRule<CourseStalenessContext, bool>[] CourseStaleRules =
    [
        new LatchedEmptyCourseStaleRule(),
        new LatchedDeferredLegacyCourseStaleRule(),
        new ExpectedEmptyCourseFreshRule(),
        new NormalizedCourseMismatchStaleRule(),
    ];

    internal static bool ChargeMarkersStale(string? charge) =>
        RuleChain.AnyMatch(charge, ChargeStaleRules);

    internal static bool CourseMarkersStale(string? latchedCourse, string? expectedCourse) =>
        RuleChain.FirstMatch(
            new CourseStalenessContext(latchedCourse, expectedCourse),
            CourseStaleRules);

    internal readonly record struct CourseStalenessContext(string? Latched, string? Expected);

    sealed class ChargeEmptyStaleRule : IRule<string?, bool>
    {
        public bool Applies(string? charge) => string.IsNullOrWhiteSpace(charge);
        public bool Select(string? charge) => true;
    }

    sealed class ChargeForbiddenMarkerStaleRule : IRule<string?, bool>
    {
        public bool Applies(string? charge) => charge is { Length: > 0 };

        public bool Select(string? charge)
        {
            foreach (var bad in ForbiddenChargeMarkers)
            {
                if (charge!.Contains(bad, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    sealed class ChargeRequiredMarkerStaleRule : IRule<string?, bool>
    {
        public bool Applies(string? charge) => charge is { Length: > 0 };

        public bool Select(string? charge)
        {
            foreach (var req in RequiredChargeMarkers)
            {
                if (!charge!.Contains(req, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    sealed class LatchedEmptyCourseStaleRule : IRule<CourseStalenessContext, bool>
    {
        public bool Applies(CourseStalenessContext ctx) => string.IsNullOrWhiteSpace(ctx.Latched);
        public bool Select(CourseStalenessContext ctx) => ctx.Expected is { Length: > 0 };
    }

    sealed class LatchedDeferredLegacyCourseStaleRule : IRule<CourseStalenessContext, bool>
    {
        public bool Applies(CourseStalenessContext ctx) =>
            ctx.Latched is { Length: > 0 }
            && ctx.Latched.Contains("DEFERRED", StringComparison.OrdinalIgnoreCase);

        public bool Select(CourseStalenessContext ctx)
        {
            foreach (var raw in ctx.Latched!.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (IdePressureChannel.IsLegacyGlassFirstPriorityLine(raw.Trim()))
                    return true;
            }

            return false;
        }
    }

    sealed class ExpectedEmptyCourseFreshRule : IRule<CourseStalenessContext, bool>
    {
        public bool Applies(CourseStalenessContext ctx) =>
            !string.IsNullOrWhiteSpace(ctx.Latched) && string.IsNullOrWhiteSpace(ctx.Expected);

        public bool Select(CourseStalenessContext ctx) => false;
    }

    sealed class NormalizedCourseMismatchStaleRule : IRule<CourseStalenessContext, bool>
    {
        public bool Applies(CourseStalenessContext ctx) =>
            !string.IsNullOrWhiteSpace(ctx.Latched) && !string.IsNullOrWhiteSpace(ctx.Expected);

        public bool Select(CourseStalenessContext ctx)
        {
            var normalizedLatched = NormalizeCourse(ctx.Latched!);
            var normalizedExpected = NormalizeCourse(ctx.Expected!);
            return !string.Equals(normalizedLatched, normalizedExpected, StringComparison.Ordinal);
        }
    }

    static string NormalizeCourse(string course) =>
        IdePressureChannel.PurgeLegacyGlassFirstLines(
            IdePressureChannel.EnsureCourseCriteria(course) ?? course).Trim();
}
