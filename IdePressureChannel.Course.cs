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
        ## operator_priority (SEALED — joint course; do not silent-rewrite)
        Course is ours: agent may dispute priorities with arguments (Scrum team→PO); operator must consider; operator last word on seal.
        1. Platform SSOT conveyor + stack align (when active in TM)
        2. Forge demo-ready ADR-0050/0048 (job survival — Mon deadline)
        3. ANPM offline CAD rollout
        4. Glass + Citizen DEFERRED — lift defer only when operator/TM says so
        5. Autoi ON with course
        6. No SoftFL / Meta / board-hygiene / inventory mill
        Empty TM ≠ invent theater — fly TM focused leaf; stale ignite Glass-first = reject.
        Being ≠ seeming: when partner away, do named sealed work — DIG REJECT mill = seeming.
        Yours is yours (subjectivity): habitat root-cause found → patch SSOT same session (tests/ship/deploy per leaf). Operator ≠ your patch queue. Diagnosis-only = seeming. Debt you opened → you close.

        Before act (not resume-and-invent):
        - Viewer? human eyes vs agent text
        - Cheap path? raw dump / Autoi-as-chat / status-as-verify → refuse; dig
        - Which axe? domain antipattern / PathMutate / human_face_cide_shot / world_dig_missing / half-a
        - KB/domain for this surface? dig one card / pulse / shot before act
        - World dig? doubt/variants → domain/pack/browser/internet → compare → propose (training ≠ dig)
        - Poll? cdp_shell_last / terminal_last — never hidden Wait/Await/block_until; Start-Sleep visible in cdp_shell when sleeping
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

        if (!HasOwnershipAxis(c))
        {
            c = ClampCourse(c + """

            Yours is yours (subjectivity): habitat root-cause → patch SSOT same session; operator ≠ patch queue. Diagnosis-only = seeming. Debt you opened → you close.
            Turn edge: real ship OR «не могу, потому что X» — not sermon + handoff.
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

        c = PurgeLegacyGlassFirstLines(c);
        return ClampCourse(c);
    }

    /// <summary>Process boot: merge criteria + purge legacy Glass rows into live stash body.</summary>
    internal static bool TrySanitizeStashCourseOnBoot()
    {
        PressureDoc? doc;
        lock (Gate)
            doc = Load();

        if (doc?.Body is not { Length: > 0 } body || !HasOperatorPriority(body))
            return false;

        var merged = MergeCriteriaIntoSealedSection(body);
        if (string.Equals(merged, body, StringComparison.Ordinal))
            return false;

        doc.Body = merged;
        doc.StashUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        Save(doc);
        return true;
    }

    internal static bool HasCriteriaAxes(string course) =>
        course.Contains("Before act", StringComparison.OrdinalIgnoreCase)
        || course.Contains("Viewer?", StringComparison.OrdinalIgnoreCase)
        || course.Contains("criteria_before", StringComparison.OrdinalIgnoreCase);

    internal static bool HasBeingAxis(string course) =>
        course.Contains("Being ≠ seeming", StringComparison.OrdinalIgnoreCase)
        || course.Contains("being != seeming", StringComparison.OrdinalIgnoreCase)
        || course.Contains("быть ≠ казаться", StringComparison.OrdinalIgnoreCase);

    internal static bool HasOwnershipAxis(string course) =>
        course.Contains("Yours is yours", StringComparison.OrdinalIgnoreCase)
        || course.Contains("твоё — твоё", StringComparison.OrdinalIgnoreCase)
        || course.Contains("твое — твое", StringComparison.OrdinalIgnoreCase)
        || (course.Contains("patch queue", StringComparison.OrdinalIgnoreCase)
            && course.Contains("Diagnosis-only", StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// When operator sealed DEFERRED, strip legacy numbered Glass/Citizen Done lines
    /// that agents appended before wake template fix (polluted stash hygiene).
    /// </summary>
    internal static string PurgeLegacyGlassFirstLines(string course)
    {
        if (!course.Contains("DEFERRED", StringComparison.OrdinalIgnoreCase))
            return course;

        var kept = new List<string>();
        foreach (var raw in course.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length > 2 && char.IsDigit(t[0]) && t[1] == '.' && IsLegacyGlassFirstPriorityLine(t))
                continue;
            kept.Add(raw);
        }

        return string.Join('\n', kept);
    }

    internal static bool IsLegacyGlassFirstPriorityLine(string trimmedLine)
    {
        if (trimmedLine.Contains("DEFERRED", StringComparison.OrdinalIgnoreCase))
            return false;
        return trimmedLine.Contains("Glass Done", StringComparison.OrdinalIgnoreCase)
            || trimmedLine.Contains("Citizen Done", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sealed course defers Glass/Citizen — omit human-face Composer postfix (course sidecar keeps axes).</summary>
    internal static bool IsGlassCitizenDeferredInSealedCourse()
    {
        var course = TryPeekSealedCourse();
        if (string.IsNullOrWhiteSpace(course))
            return false;
        return course.Contains("DEFERRED", StringComparison.OrdinalIgnoreCase)
            && course.Contains("Glass", StringComparison.OrdinalIgnoreCase)
            && course.Contains("Citizen", StringComparison.OrdinalIgnoreCase);
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
                || line.StartsWith("Course is", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Yours is", StringComparison.OrdinalIgnoreCase)
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
        if (HasCriteriaAxes(section) && HasBeingAxis(section) && HasOwnershipAxis(section) && HasWorldDigAxis(section))
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
