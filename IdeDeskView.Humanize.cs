#nullable enable

namespace CdpMcp;

internal static partial class IdeDeskView
{
    /// <summary>Board-friendly pulse: drop schema noise.</summary>
    public static string HumanizePulse(string raw, string? organ)
    {
        var s = TrimLine(raw, 96);

        // correspondence/v0 FAIL path_required → need path
        if (s.Contains(" FAIL ", StringComparison.Ordinal)
            || s.Contains("FAIL ", StringComparison.Ordinal))
        {
            if (s.Contains("path_required", StringComparison.OrdinalIgnoreCase)
                || s.Contains("no project", StringComparison.OrdinalIgnoreCase)
                || s.Contains("workspace_path", StringComparison.OrdinalIgnoreCase))
                return "need cdp_open";

            var failAt = s.IndexOf("FAIL", StringComparison.Ordinal);
            if (failAt >= 0)
            {
                var reason = s[(failAt + 4)..].Trim().TrimStart(':').Trim();
                if (reason.Length > 0)
                    return TrimLine("fail " + reason, 40);
            }

            return "fail";
        }

        // editor_scene/v0 ok n=0 dirty=0 disk=0 → 0 buf
        if (s.Contains("/v", StringComparison.Ordinal) && s.Contains(" ok ", StringComparison.Ordinal))
        {
            var okAt = s.IndexOf(" ok ", StringComparison.Ordinal);
            if (okAt >= 0)
            {
                var rest = s[(okAt + 4)..].Trim();
                if (rest.Length > 0)
                    return TrimLine(HumanizeCounts(rest), 56);
            }
        }

        if (s.Contains("n=", StringComparison.Ordinal) && s.Contains("dirty=", StringComparison.Ordinal))
            return TrimLine(HumanizeCounts(s), 56);

        if (s.StartsWith("internet_browser", StringComparison.OrdinalIgnoreCase))
        {
            if (s.Contains("tabs=", StringComparison.Ordinal))
            {
                var i = s.IndexOf("tabs=", StringComparison.Ordinal);
                return TrimLine(s[i..], 40);
            }

            return "idle";
        }

        if (s.Contains("no project", StringComparison.OrdinalIgnoreCase)
            || s.Contains("no_project", StringComparison.OrdinalIgnoreCase))
            return "no project — cdp_open";

        if (s.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            && ShortOrgan(organ) == "git")
            return "need cdp_open";

        return TrimLine(s, 56);
    }

    /// <summary>n=0 dirty=0 disk=0 → 0 buf; n=3 dirty=1 → 3 buf ·1dirty</summary>
    public static string HumanizeCounts(string rest)
    {
        static int? Grab(string s, string key)
        {
            var i = s.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var start = i + key.Length;
            var end = start;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            if (end == start) return null;
            return int.TryParse(s[start..end], out var n) ? n : null;
        }

        var n = Grab(rest, "n=");
        var dirty = Grab(rest, "dirty=");
        if (n is null) return rest;
        if (n == 0)
            return "—";

        var line = $"{n} buf";
        if (dirty is > 0)
            line += $" ·{dirty}dirty";
        return line;
    }

}
