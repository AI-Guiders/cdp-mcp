#nullable enable

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>
    /// Agent habit: <c>feature X; task Y; start</c> in one <c>cmd=</c> — bakes junk titles.
    /// Refuse when a later <c>;</c>-segment starts with a board verb.
    /// </summary>
    internal static object? RefuseChainedBoardCmd(string raw)
    {
        var segs = SplitCmdSegments(raw);
        if (segs.Count < 2)
            return null;

        for (var i = 1; i < segs.Count; i++)
        {
            var seg = segs[i].Trim();
            if (seg.Length == 0)
                continue;
            var head = FirstCmdToken(seg);
            if (head.Length == 0)
                continue;
            if (IsChainableBoardHead(head))
            {
                return Err(
                    "multi_cmd",
                    "one verb per cmd= — not feature X; task Y; start (run separately)");
            }
        }

        return null;
    }

    /// <summary>Title already baked with <c>; task</c> / <c>; start</c> — refuse seed.</summary>
    internal static string? ChainedTitleHint(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.IndexOf(';') < 0)
            return null;
        foreach (var seg in title.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var head = FirstCmdToken(seg);
            if (IsChainableBoardHead(head))
                return "one verb per cmd= — not feature X; task Y; start";
        }

        return null;
    }

    static bool IsChainableBoardHead(string head)
    {
        var h = head.ToLowerInvariant();
        return h is "feature" or "intent"
            or "task" or "add" or "stage"
            or "focus" or "done" or "complete"
            or "start" or "shipped" or "completed"
            or "drop" or "rm" or "delete"
            or "park" or "parked" or "defer" or "deferred"
            or "product" or "category" or "executor" or "assignee" or "lane" or "focus_lane" or "phase"
            or "events" or "note" or "review" or "reviews" or "remark" or "remarks" or "rr"
            or "criteria" or "criterion"
            or "start_phase" or "phase_start"
            or "complete_phase" or "phase_complete" or "end_phase"
            or "leftover" or "sweep" or "leftovers"
            or "change_plan" or "changeplan" or "cp"
            or "await_operator" or "await_partner";
    }

    static string FirstCmdToken(string segment)
    {
        var s = segment.Trim();
        if (s.Length == 0)
            return "";
        var end = 0;
        while (end < s.Length && !char.IsWhiteSpace(s[end]))
            end++;
        return s[..end];
    }

    /// <summary>Split on <c>;</c> outside quotes (parity with Tokenize quotes).</summary>
    static List<string> SplitCmdSegments(string line)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuote = false;
        char quote = '\0';
        foreach (var ch in line)
        {
            if (inQuote)
            {
                if (ch == quote) { inQuote = false; sb.Append(ch); continue; }
                sb.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuote = true;
                quote = ch;
                sb.Append(ch);
                continue;
            }

            if (ch == ';')
            {
                list.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
            list.Add(sb.ToString());
        return list;
    }
}
