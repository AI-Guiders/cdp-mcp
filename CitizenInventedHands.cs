#nullable enable
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// SoftFL: FM invents «Сделала: find …» without @intent → no RouteHost execute →
/// no peerAck → no CitizenResultWake (auto-wake feel broken). Recover claims into routes.
/// </summary>
internal static partial class CitizenInventedHands
{
    static readonly Regex HandsLine = HandsLineRegex();

    /// <summary>True when prose claims harness hands without needing executed[].</summary>
    public static bool LooksLikeHandsClaim(string? prose)
    {
        if (string.IsNullOrWhiteSpace(prose))
            return false;
        foreach (var raw in prose.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.Trim();
            if (HandsLine.IsMatch(t))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parse «Сделала: find X · files tree …» into RouteOne intents (max 6).
    /// Skips tips that are already organ Go labels (find_desk / files_desk).
    /// </summary>
    public static IReadOnlyList<CitizenIntentRouter.Route> TryRecoverRoutes(string? prose)
    {
        if (string.IsNullOrWhiteSpace(prose))
            return [];

        var list = new List<CitizenIntentRouter.Route>();
        foreach (var raw in prose.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.Trim();
            var m = HandsLine.Match(t);
            if (!m.Success)
                continue;

            var payload = m.Groups[1].Value.Trim();
            if (payload.Length == 0)
                continue;

            foreach (var part in payload.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (list.Count >= 6)
                    break;
                if (IsOrganGoLabel(part))
                    continue;

                // SoftFL: invent «desk layout=(P/M)(F)» / topology=… without presentation_set.
                if (TryNormalizeInventedPresentation(part) is { } presentationClaim)
                {
                    var pres = CitizenIntentRouter.RouteOne(presentationClaim);
                    if (pres.Ok)
                    {
                        list.Add(pres);
                        continue;
                    }
                }

                var route = CitizenIntentRouter.RouteOne(part);
                if (route.Ok)
                    list.Add(route);
            }
        }

        return list;
    }

    static bool IsOrganGoLabel(string part)
    {
        var p = part.Trim();
        if (p.Length == 0)
            return true;
        // Real FormatHands Go=find_desk / files_desk — not recoverable as intent text.
        if (p.EndsWith("_desk", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.Equals("files", StringComparison.OrdinalIgnoreCase)
            || p.Equals("find", StringComparison.OrdinalIgnoreCase)
            || p.Equals("shell", StringComparison.OrdinalIgnoreCase)
            || p.Equals("поиск", StringComparison.OrdinalIgnoreCase))
            return true;
        // Truncation crumbs from FormatHands tips.
        if (p.Contains('…') && p.Length < 24)
            return true;
        return false;
    }

    /// <summary>
    /// Lived invent: desk layout= / topology= prose → presentation_set.
    /// </summary>
    static string? TryNormalizeInventedPresentation(string part)
    {
        var p = part.Trim().Trim('"', '\'');
        if (p.Length == 0)
            return null;

        if (p.StartsWith("presentation_set", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("cide_presentation", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("presentation ", StringComparison.OrdinalIgnoreCase))
            return null;

        var topology = TryExtractTopologyToken(p);
        if (string.IsNullOrWhiteSpace(topology))
            return null;

        return "presentation_set topology=" + topology;
    }

    static string? TryExtractTopologyToken(string p)
    {
        foreach (var key in new[] { "layout", "topology" })
        {
            var idx = p.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;
            var after = p[(idx + key.Length)..].TrimStart();
            if (after.StartsWith('=') || after.StartsWith(':'))
                after = after[1..].TrimStart();
            after = after.TrimStart('"', '\'');
            var open = after.IndexOf('(');
            if (open < 0)
                continue;

            // Canon topologies are concatenations: (P/M)(F) · (P)(F)(M).
            var i = open;
            while (i < after.Length && after[i] == '(')
            {
                var depth = 0;
                var j = i;
                for (; j < after.Length; j++)
                {
                    if (after[j] == '(')
                        depth++;
                    else if (after[j] == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            j++;
                            break;
                        }
                    }
                }

                if (depth != 0)
                    break;
                i = j;
                while (i < after.Length && char.IsWhiteSpace(after[i]))
                    i++;
            }

            if (i > open)
                return after[open..i];
        }

        return null;
    }


    [GeneratedRegex(
        @"^Сделал[аи]?\s*:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HandsLineRegex();
}
