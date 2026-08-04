#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Sealed operator course from pressure stash — Autoi wake habitat SSOT (not Composer body dump).
/// Criteria-before-act lives INSIDE operator_priority so Extract keeps it (next ## would truncate).
/// </summary>
internal static partial class IdePressureChannel
{
    public const int MaxSealedCourseChars = 1600;

    /// <summary>
    /// Habitat default when stash forgot SEALED — wake must not fall back to resume-and-invent.
    /// Criteria axes are inside this section (not a sibling ##).
    /// </summary>
    internal const string CanonicalSealedCourse =
        """
        ## operator_priority (SEALED — do not rewrite)
        1. Glass Done (human flight)
        2. Citizen Done stable → 15.08
        3. Autoi ON with course
        4. No SoftFL / Meta / board-hygiene / inventory mill
        Empty TM ≠ invent theater — invent only on real product gap under Glass Done.

        Before act (not resume-and-invent):
        - Viewer? human eyes vs agent text
        - Cheap path? raw dump / Autoi-as-chat / status-as-verify → refuse; dig
        - Which axe? domain antipattern / PathMutate / human_face_cide_shot / half-a
        - KB/domain for this surface? dig one card / pulse / shot before act
        Ontology lives in habitat (course + refuse) — not polite agreement.
        """;

    /// <summary>Tests: force sealed course without seat stash.</summary>
    internal static string? SealedCourseOverrideForTests { get; set; }

    /// <summary>
    /// Peek sealed course for Autoi wake — stash section, else CanonicalSealedCourse.
    /// </summary>
    internal static string? TryPeekSealedCourse()
    {
        if (SealedCourseOverrideForTests is { } o)
            return string.IsNullOrWhiteSpace(o) ? null : EnsureCourseCriteria(o.Trim());

        try
        {
            var fromStash = ExtractSealedCourse(Load()?.Body);
            return EnsureCourseCriteria(fromStash) ?? EnsureCourseCriteria(CanonicalSealedCourse);
        }
        catch
        {
            return EnsureCourseCriteria(CanonicalSealedCourse);
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

    /// <summary>
    /// Course without before-act axes → append canonical criteria block (inside same section).
    /// </summary>
    internal static string? EnsureCourseCriteria(string? course)
    {
        if (string.IsNullOrWhiteSpace(course))
            return null;

        var c = course.Trim();
        if (HasCriteriaAxes(c))
            return ClampCourse(c);

        var append =
            """

            Before act (not resume-and-invent):
            - Viewer? human eyes vs agent text
            - Cheap path? raw dump / Autoi-as-chat / status-as-verify → refuse; dig
            - Which axe? domain antipattern / PathMutate / human_face_cide_shot / half-a
            - KB/domain for this surface? dig one card / pulse / shot before act
            Ontology lives in habitat (course + refuse) — not polite agreement.
            """;
        return ClampCourse(c + append);
    }

    internal static bool HasCriteriaAxes(string course) =>
        course.Contains("Before act", StringComparison.OrdinalIgnoreCase)
        || course.Contains("Viewer?", StringComparison.OrdinalIgnoreCase)
        || course.Contains("criteria_before", StringComparison.OrdinalIgnoreCase);

    internal static bool HasOperatorPriority(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Contains("## operator_priority", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Soft-refuse stash that drops SEALED course (night wipe → resume-and-invent). force= escape.
    /// </summary>
    internal static void RefuseStashDroppingSealedCourse(
        string? previousBody,
        string newBody,
        IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (ForceArg(args))
            return;
        if (!HasOperatorPriority(previousBody))
            return;
        if (HasOperatorPriority(newBody))
            return;

        throw new ArgumentException(
            "stash refused — sealed_course_drop: body drops ## operator_priority (SEALED). " +
            "Keep sealed course + criteria-before-act; put agent notes under ## agent_state. force=true escape.");
    }

    /// <summary>Body without sealed section → prepend CanonicalSealedCourse (axe, not sermon).</summary>
    internal static string EnsureStashHasSealedCourse(string body)
    {
        var trimmed = body.Trim();
        if (!HasOperatorPriority(trimmed))
            return CanonicalSealedCourse.Trim() + "\n\n## agent_state\n" + trimmed;

        return MergeCriteriaIntoSealedSection(trimmed);
    }

    static string MergeCriteriaIntoSealedSection(string body)
    {
        var text = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var idx = text.IndexOf("## operator_priority", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return CanonicalSealedCourse.Trim() + "\n\n## agent_state\n" + body.Trim();

        var rest = text[idx..];
        var next = rest.IndexOf("\n## ", StringComparison.Ordinal);
        var section = (next < 0 ? rest : rest[..next]).TrimEnd();
        var after = next < 0 ? "" : rest[next..];
        if (HasCriteriaAxes(section))
            return text;

        var merged = EnsureCourseCriteria(section) ?? section;
        return text[..idx] + merged + after;
    }

    static string ClampCourse(string section)
    {
        if (section.Length <= MaxSealedCourseChars)
            return section;
        return section[..MaxSealedCourseChars].TrimEnd() + "…";
    }

    static bool ForceArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;
        if (args.TryGetValue("force", out var el))
        {
            if (el.ValueKind == JsonValueKind.True)
                return true;
            if (el.ValueKind == JsonValueKind.String
                && bool.TryParse(el.GetString(), out var b)
                && b)
                return true;
        }

        return false;
    }
}
