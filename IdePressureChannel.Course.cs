#nullable enable

namespace CdpMcp;

/// <summary>
/// Sealed operator course from pressure stash — Autoi wake habitat SSOT (not Composer body dump).
/// </summary>
internal static partial class IdePressureChannel
{
    public const int MaxSealedCourseChars = 1600;

    /// <summary>Tests: force sealed course without seat stash.</summary>
    internal static string? SealedCourseOverrideForTests { get; set; }

    /// <summary>
    /// Peek <c>## operator_priority</c> (SEALED) from hot stash body — empty when absent.
    /// </summary>
    internal static string? TryPeekSealedCourse()
    {
        if (SealedCourseOverrideForTests is { } o)
            return string.IsNullOrWhiteSpace(o) ? null : o.Trim();

        try
        {
            var body = Load()?.Body;
            return ExtractSealedCourse(body);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extract sealed operator_priority section from stash markdown.</summary>
    internal static string? ExtractSealedCourse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var text = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var idx = text.IndexOf("## operator_priority", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var rest = text[idx..];
        var next = rest.IndexOf("\n## ", StringComparison.Ordinal);
        var section = next < 0 ? rest : rest[..next];
        section = section.Trim();
        if (section.Length == 0)
            return null;

        if (section.Length <= MaxSealedCourseChars)
            return section;
        return section[..MaxSealedCourseChars].TrimEnd() + "…";
    }
}
