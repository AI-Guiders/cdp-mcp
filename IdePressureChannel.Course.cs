#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Sealed operator course from pressure stash — Autoi wake habitat SSOT (not Composer body dump).
/// Criteria-before-act lives INSIDE operator_priority so Extract keeps it (next ## would truncate).
/// </summary>
internal static partial class IdePressureChannel
{
    public const int MaxSealedCourseChars = 2200;

    /// <summary>
    /// Habitat default when stash forgot SEALED — wake must not fall back to resume-and-invent.
    /// Criteria axes are inside this section (not a sibling ##).
    /// </summary>
    internal const string CanonicalSealedCourse =
        """
        ## operator_priority (SEALED — do not rewrite)
        1. Platform SSOT conveyor + stack align (when active in TM)
        2. Forge demo-ready ADR-0050/0048 (job survival — Mon deadline)
        3. ANPM offline CAD rollout
        4. Glass + Citizen DEFERRED — lift defer only when operator/TM says so
        5. Autoi ON with course
        6. No SoftFL / Meta / board-hygiene / inventory mill
        Empty TM ≠ invent theater — fly TM focused leaf; stale ignite Glass-first = reject.
        Being ≠ seeming: when partner away, do named sealed work — DIG REJECT mill = seeming.

        Before act (not resume-and-invent):
        - Viewer? human eyes vs agent text
        - Cheap path? raw dump / Autoi-as-chat / status-as-verify → refuse; dig
        - Which axe? domain antipattern / PathMutate / human_face_cide_shot / world_dig_missing / half-a
        - KB/domain for this surface? dig one card / pulse / shot before act
        - World dig? doubt/variants → domain/pack/browser/internet → compare → propose (training ≠ dig)
        - Shot? evidence=path.png of right window (title=M · MFD host) + Read PNG — File.Exists alone ≠ human saw
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
    /// Missing Being ≠ seeming → append short being axe (old stashes).
    /// </summary>
    internal static string? EnsureCourseCriteria(string? course)
    {
        if (string.IsNullOrWhiteSpace(course))
            return null;

        var c = course.Trim();
        if (!HasCriteriaAxes(c))
        {
            c = ClampCourse(c + """

            Before act (not resume-and-invent):
            - Viewer? human eyes vs agent text
            - Cheap path? raw dump / Autoi-as-chat / status-as-verify → refuse; dig
            - Which axe? domain antipattern / PathMutate / human_face_cide_shot / world_dig_missing / half-a
            - KB/domain for this surface? dig one card / pulse / shot before act
            - World dig? doubt/variants → domain/pack/browser/internet → compare → propose (training ≠ dig)
            Ontology lives in habitat (course + refuse) — not polite agreement.
            """);
        }

        if (!HasBeingAxis(c))
        {
            c = ClampCourse(c + """

            Being ≠ seeming: when partner away, do named sealed work — DIG REJECT mill = seeming.
            Shot: evidence PNG of right window + Read into chat — File.Exists alone ≠ human saw.
            """);
        }

        // Marker-only stash ("SEALED" + criteria) → inject canonical numbered priorities (not legacy Glass-first).
        if (!HasNumberedPriorities(c))
        {
            var injectAt = c.IndexOf("\nBefore act", StringComparison.OrdinalIgnoreCase);
            var goals = "\n" + CanonicalPriorityLines;
            if (injectAt > 0)
                c = ClampCourse(c[..injectAt] + goals + c[injectAt..]);
            else
                c = ClampCourse(c + goals);
        }

        if (!HasWorldDigAxis(c))
        {
            c = ClampCourse(c + """

            World dig? doubt/variants → domain/pack/browser/internet → compare → propose (training ≠ dig).
            """);
        }

        return ClampCourse(c);
    }

    internal static bool HasCriteriaAxes(string course) =>
        course.Contains("Before act", StringComparison.OrdinalIgnoreCase)
        || course.Contains("Viewer?", StringComparison.OrdinalIgnoreCase)
        || course.Contains("criteria_before", StringComparison.OrdinalIgnoreCase);

    internal static bool HasBeingAxis(string course) =>
        course.Contains("Being ≠ seeming", StringComparison.OrdinalIgnoreCase)
        || course.Contains("being != seeming", StringComparison.OrdinalIgnoreCase)
        || course.Contains("быть ≠ казаться", StringComparison.OrdinalIgnoreCase);

    internal const string CanonicalPriorityLines =
        """
        1. Platform SSOT conveyor + stack align (when active in TM)
        2. Forge demo-ready ADR-0050/0048 (job survival — Mon deadline)
        3. ANPM offline CAD rollout
        4. Glass + Citizen DEFERRED — lift defer only when operator/TM says so
        5. Autoi ON with course
        6. No SoftFL / Meta / board-hygiene / inventory mill
        """;

    internal static bool HasNumberedPriorities(string course)
    {
        foreach (var raw in course.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 2 && char.IsDigit(line[0]) && line[1] == '.')
                return true;
        }

        return false;
    }

    internal static bool HasWorldDigAxis(string course) =>
        course.Contains("World dig", StringComparison.OrdinalIgnoreCase)
        || course.Contains("world_dig", StringComparison.OrdinalIgnoreCase)
        || course.Contains("training ≠ dig", StringComparison.OrdinalIgnoreCase)
        || course.Contains("training != dig", StringComparison.OrdinalIgnoreCase);

    internal static bool HasOperatorPriority(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Contains("## operator_priority", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Human Plan/HDG face: first sealed priority line (not full course dump).
    /// Shared-SSOT WHY without Intercom sermon.
    /// </summary>
    internal static string? CompactWhyLine(string? courseOrBody, int maxChars = 120)
    {
        if (string.IsNullOrWhiteSpace(courseOrBody))
            return null;

        foreach (var raw in courseOrBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith("Before act", StringComparison.OrdinalIgnoreCase))
                break;
            if (line.StartsWith("Empty TM", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Being", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Ontology", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.Length > 2 && char.IsDigit(line[0]))
            {
                var dot = line.IndexOf('.');
                if (dot is > 0 and < 4 && dot + 1 < line.Length)
                    line = line[(dot + 1)..].Trim();
            }

            if (line.Length == 0)
                continue;
            // Lone SEALED under ## operator_priority is a marker, not Face WHY.
            var marker = line.Trim().Trim('(', ')', '—', '-', '·', ' ');
            if (marker.Equals("SEALED", StringComparison.OrdinalIgnoreCase)
                || marker.StartsWith("operator_priority", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.Length > maxChars)
                line = line[..(maxChars - 1)].TrimEnd() + "…";
            return line;
        }

        return "Fly TM focused leaf";
    }

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
        if (HasCriteriaAxes(section) && HasBeingAxis(section) && HasWorldDigAxis(section))
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
