#nullable enable

namespace CdpMcp;

/// <summary>
/// Human Plan Face projection — agent sealed course / TM titles stay SSOT;
/// Face WHY/NEXT never dump SoftFL refuse mills or "operator eyes" theatre.
/// </summary>
internal static class IdeHumanFacePlan
{
    /// <summary>Plan WHY card — human goal line from sealed course.</summary>
    internal static string? WhyLine(string? courseOrBody, int maxChars = 120)
    {
        if (string.IsNullOrWhiteSpace(courseOrBody))
            return null;

        string? firstHuman = null;
        string? glassGoal = null;
        string? citizenGoal = null;

        foreach (var raw in courseOrBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = NormalizePriorityLine(raw);
            if (line is null)
                continue;
            if (IsBeforeActFence(line))
                break;
            if (IsAgentMetaLine(line) || IsSealedMarker(line))
                continue;

            if (LooksLikeAgentJargon(line))
            {
                // Still harvest human goals buried inside a refuse mill line.
                if (glassGoal is null && ContainsIgnore(line, "Glass Done"))
                    glassGoal = "Glass Done — instruments people can fly";
                if (citizenGoal is null && ContainsIgnore(line, "Citizen"))
                    citizenGoal = "Citizen stable toward 15.08";
                continue;
            }

            firstHuman ??= line;
            if (glassGoal is null && ContainsIgnore(line, "Glass") && !ContainsIgnore(line, "DEFERRED"))
                glassGoal = Truncate(line, maxChars);
            if (citizenGoal is null && ContainsIgnore(line, "Citizen") && !ContainsIgnore(line, "DEFERRED"))
                citizenGoal = Truncate(line, maxChars);
        }

        var pick = firstHuman ?? glassGoal ?? citizenGoal;
        if (pick is null)
            pick = IdePressureChannel.CompactWhyLine(courseOrBody);
        if (pick is null)
        {
            if (ContainsIgnore(courseOrBody, "Forge"))
                pick = "Forge demo-ready ADR-0050/0048";
            else if (ContainsIgnore(courseOrBody, "Platform") || ContainsIgnore(courseOrBody, "guiders-platform"))
                pick = "Platform SSOT conveyor + stack align";
            else if (ContainsIgnore(courseOrBody, "Glass Done"))
                pick = "Glass Done — instruments people can fly";
            else if (ContainsIgnore(courseOrBody, "Citizen"))
                pick = "Citizen stable toward 15.08";
            else
                pick = "Fly TM focused leaf";
        }

        pick = StripFaceTheatre(pick);
        if (LooksLikeAgentJargon(pick) || IsSealedMarker(pick) || string.IsNullOrWhiteSpace(pick))
            pick = IdePressureChannel.CompactWhyLine(courseOrBody) ?? "Fly TM focused leaf";

        return Truncate(pick, maxChars);
    }

    /// <summary>Plan NEXT glance — strip agent refuse tags; keep the human move.</summary>
    internal static string NextLeaf(string? taskOrFeature, int maxChars = 72)
    {
        var full = (taskOrFeature ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (full.Length == 0)
            return "No active leaf.";

        var cleaned = StripActTags(full);
        cleaned = StripFaceTheatre(cleaned);

        // "… invent-only Hold — human substance" → substance
        var em = cleaned.IndexOf(" — ", StringComparison.Ordinal);
        if (em > 0 && em + 3 < cleaned.Length
            && (ContainsIgnore(cleaned, "invent-only") || ContainsIgnore(cleaned, "SoftFL")
                || cleaned.StartsWith("Dig densest", StringComparison.OrdinalIgnoreCase)))
        {
            var after = cleaned[(em + 3)..].Trim();
            after = StripFaceTheatre(after);
            if (after.Length is >= 8 and <= 96 && !LooksLikeAgentJargon(after))
                cleaned = after;
        }

        cleaned = cleaned.Trim();
        if (cleaned.Length == 0 || LooksLikeAgentJargon(cleaned))
            return Truncate(StripFaceTheatre(full), maxChars);

        return Truncate(cleaned, maxChars);
    }

    /// <summary>Plan pulse / chrome_hint for Face — drop ShowFace SoftFL mill.</summary>
    internal static string PulseLine(string? pulse, int maxChars = 96)
    {
        if (string.IsNullOrWhiteSpace(pulse))
            return "no plan";
        var s = StripFaceTheatre(pulse.Trim());
        s = StripShowFaceSoftFl(s);
        if (LooksLikeAgentJargon(s))
        {
            var waveAt = s.IndexOf("wave", StringComparison.OrdinalIgnoreCase);
            if (waveAt >= 0)
            {
                var head = s[waveAt..];
                var cut = head.IndexOf(" · local", StringComparison.OrdinalIgnoreCase);
                if (cut > 0)
                    head = head[..cut];
                head = StripFaceTheatre(StripShowFaceSoftFl(head));
                if (!LooksLikeAgentJargon(head) && head.Length >= 8)
                    return Truncate(head, maxChars);
            }

            return "Plan · flying";
        }

        return Truncate(s, maxChars);
    }

    /// <summary>TM board line for Face latch.</summary>
    internal static string BoardLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line ?? "";
        return NextLeaf(StripShowFaceSoftFl(StripFaceTheatre(line.Trim())), maxChars: 96);
    }

    static string StripShowFaceSoftFl(string s)
    {
        foreach (var needle in new[]
                 {
                     "ShowFace Place+attention SoftFL",
                     "ShowFace Place+attention So",
                     "ShowFace Place+attention"
                 })
        {
            var i = s.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                continue;
            var before = s[..i].Trim().TrimEnd('·', '-', '—', ' ', '/');
            var after = s[(i + needle.Length)..].TrimStart('.', '…', ' ', 'F', 'L');
            var cut = after.IndexOf(" · ", StringComparison.Ordinal);
            after = cut >= 0 ? after[cut..].TrimStart('·', ' ') : "";
            s = string.IsNullOrWhiteSpace(before)
                ? after
                : string.IsNullOrWhiteSpace(after)
                    ? before
                    : before + " · " + after;
        }

        return s.Replace("  ", " ", StringComparison.Ordinal).Trim('·', ' ', '-');
    }

    static string? NormalizePriorityLine(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            return null;
        if (line.StartsWith("Empty TM", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Being", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Ontology", StringComparison.OrdinalIgnoreCase))
            return null;

        if (line.Length > 2 && char.IsDigit(line[0]))
        {
            var dot = line.IndexOf('.');
            if (dot is > 0 and < 4 && dot + 1 < line.Length)
                line = line[(dot + 1)..].Trim();
        }

        return line.Length == 0 ? null : line;
    }

    static bool IsBeforeActFence(string line) =>
        line.StartsWith("Before act", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stash often writes a lone SEALED marker under ## operator_priority — not a human WHY.</summary>
    static bool IsSealedMarker(string line)
    {
        var t = line.Trim().Trim('(', ')', '—', '-', '·', ' ');
        if (t.Equals("SEALED", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.StartsWith("SEALED", StringComparison.OrdinalIgnoreCase)
            && (t.Length == 6 || t[6] is ' ' or '—' or '-' or '·' or '('))
            return true;
        return t.StartsWith("operator_priority", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsAgentMetaLine(string line) =>
        line.StartsWith("Shot:", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Shot?", StringComparison.OrdinalIgnoreCase);

    internal static bool LooksLikeAgentJargon(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;
        return ContainsIgnore(line, "SoftFL")
               || ContainsIgnore(line, "tip mill")
               || ContainsIgnore(line, "operator eyes")
               || ContainsIgnore(line, "твои глаза")
               || ContainsIgnore(line, "Meta invent")
               || ContainsIgnore(line, "DIG REJECT")
               || ContainsIgnore(line, "nested[axb]")
               || ContainsIgnore(line, "half-a")
               || ContainsIgnore(line, "world_dig_missing")
               || ContainsIgnore(line, "PathMutate")
               || ContainsIgnore(line, "human_face_cide_shot")
               || ContainsIgnore(line, "board hygiene")
               || ContainsIgnore(line, "board-hygiene");
    }

    static string StripFaceTheatre(string s)
    {
        // Drop refuse clauses that leak onto Face.
        foreach (var needle in new[]
                 {
                     "SoftFL invent REJECT",
                     "SoftFL STRUCK",
                     "SoftFL REJECT",
                     "nested[axb]",
                     "agent refuse Face Done claim",
                     "agent refuse Face Done",
                     "agent refuse #CIDE Done",
                     "agent refuse",
                     "YOUR Glass eyes",
                     "Glass eyes",
                     "Face axis4 operator",
                     "tip mill ≠ Done",
                     "tip mill != Done",
                     "Face SoftInstrument/#CIDE Done needs operator eyes",
                     "needs operator eyes",
                     "operator eyes",
                     "refuse board hygiene",
                     "board-hygiene",
                     "board hygiene"
                 })
        {
            var i = s.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                continue;
            var before = s[..i].Trim().TrimEnd('·', '-', '—', ' ', '/');
            var after = s[(i + needle.Length)..].Trim().TrimStart('·', '-', '—', ' ', '/');
            s = string.IsNullOrWhiteSpace(before)
                ? after
                : string.IsNullOrWhiteSpace(after)
                    ? before
                    : before + " · " + after;
        }

        return s.Replace("  ", " ", StringComparison.Ordinal).Trim('·', ' ', '-');
    }

    static string StripActTags(string s)
    {
        while (true)
        {
            var i = s.IndexOf("@act", StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                break;
            var j = i + 4;
            while (j < s.Length && char.IsWhiteSpace(s[j]))
                j++;
            if (j >= s.Length || s[j] != '#')
                break;
            j++;
            while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_'))
                j++;
            s = (s[..i] + s[j..]).Trim();
        }

        return s.Trim();
    }

    static bool ContainsIgnore(string hay, string needle) =>
        hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    static string Truncate(string s, int max)
    {
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (s.Length <= max)
            return s;
        return s[..(max - 1)].TrimEnd() + "…";
    }
}
