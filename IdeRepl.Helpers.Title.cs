#nullable enable

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>Split trailing <c>@act</c> / TM directive, optional <c>#CDP</c> product, optional <c>~Who</c> executor from title tokens.</summary>
    internal static (string Title, string? Phase, string? Product, string? Executor) SplitTitleMeta(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return ("", null, null, null);

        var list = tokens.ToList();
        string? product = null;
        string? executor = null;
        // Peel trailing #Product / ~Who tags (order-flexible with @phase).
        for (var guard = 0; guard < 6 && list.Count > 0; guard++)
        {
            var last = list[^1];
            if (last.StartsWith('#') && last.Length > 1 && product is null)
            {
                product = last[1..];
                list.RemoveAt(list.Count - 1);
                continue;
            }

            if (last.StartsWith('~') && last.Length > 1 && executor is null)
            {
                executor = last[1..];
                list.RemoveAt(list.Count - 1);
                continue;
            }

            break;
        }

        var (title, phase) = SplitTitlePhase(list);
        // Also allow #Product / ~Who before @phase: peel leftover from title tokens once more after phase.
        if ((product is null || executor is null) && title.Length > 0)
        {
            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            for (var guard = 0; guard < 4 && words.Count > 0; guard++)
            {
                var last = words[^1];
                if (product is null && last.StartsWith('#') && last.Length > 1)
                {
                    product = last[1..];
                    words.RemoveAt(words.Count - 1);
                    continue;
                }

                if (executor is null && last.StartsWith('~') && last.Length > 1)
                {
                    executor = last[1..];
                    words.RemoveAt(words.Count - 1);
                    continue;
                }

                break;
            }

            title = string.Join(' ', words);
        }

        return (title, phase, product, executor);
    }

    /// <summary>Split trailing <c>@act</c> phase affinity or TM directive (<c>@focus</c>/<c>@done</c>/…) from title tokens.</summary>
    internal static (string Title, string? Phase) SplitTitlePhase(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return ("", null);
        var last = tokens[^1];
        if (last.StartsWith('@')
            && last.Length > 1)
        {
            var tag = last[1..];
            if (Cdp.Core.CdpEnumParse.TryParsePhase(tag, out var p))
            {
                var title = string.Join(' ', tokens.Take(tokens.Count - 1));
                return (title.Trim(), Cdp.Core.CdpEnumParse.ToWire(p));
            }

            // feature Y @focus → title "Y" (do not bake @focus into the name)
            if (IsTitleDirective(tag))
            {
                var title = string.Join(' ', tokens.Take(tokens.Count - 1));
                return (title.Trim(), null);
            }

            // Board chrome / legacy paste — `@todo` is not a phase; do not bake into the title.
            if (tag.Equals("todo", StringComparison.OrdinalIgnoreCase))
            {
                var title = string.Join(' ', tokens.Take(tokens.Count - 1));
                return (title.Trim(), null);
            }
        }

        return (string.Join(' ', tokens).Trim(), null);
    }

    static bool IsTitleDirective(string tag) =>
        tag.Equals("focus", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("done", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("complete", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("park", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("parked", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("defer", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("deferred", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("drop", StringComparison.OrdinalIgnoreCase);

    /// <summary>Agent meant board — <c>feature list</c> / <c>task ls</c> must not upsert a junk title.</summary>
    static bool IsBoardListAlias(string title) =>
        title.Equals("list", StringComparison.OrdinalIgnoreCase)
        || title.Equals("ls", StringComparison.OrdinalIgnoreCase)
        || title.Equals("board", StringComparison.OrdinalIgnoreCase)
        || title.Equals("tasks", StringComparison.OrdinalIgnoreCase)
        || title.Equals("plan", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bare REPL verbs as the whole title — reject with a concrete hint.</summary>
    static string? ReservedTitleHint(string title, string kind)
    {
        if (title.Equals("done", StringComparison.OrdinalIgnoreCase)
            || title.Equals("complete", StringComparison.OrdinalIgnoreCase))
            return "done <title> | done";
        if (title.Equals("focus", StringComparison.OrdinalIgnoreCase))
            return "focus <title>";
        if (title.Equals("park", StringComparison.OrdinalIgnoreCase)
            || title.Equals("parked", StringComparison.OrdinalIgnoreCase))
            return "park <title> | park | task X @parked";
        if (title.Equals("defer", StringComparison.OrdinalIgnoreCase)
            || title.Equals("deferred", StringComparison.OrdinalIgnoreCase))
            return "defer <title> | defer";
        if (title.Equals("drop", StringComparison.OrdinalIgnoreCase)
            || title.Equals("rm", StringComparison.OrdinalIgnoreCase)
            || title.Equals("delete", StringComparison.OrdinalIgnoreCase))
            return "drop feature X | drop task X | drop";
        if (title.Equals("phase", StringComparison.OrdinalIgnoreCase))
            return "phase act | phase verify";
        if (title.Equals("executor", StringComparison.OrdinalIgnoreCase)
            || title.Equals("assignee", StringComparison.OrdinalIgnoreCase))
            return "executor Sierra | assignee Кир | executor clear";
        if (title.Equals("share", StringComparison.OrdinalIgnoreCase))
            return "share report | share plan";
        if (title.Equals("promote", StringComparison.OrdinalIgnoreCase))
            return "promote";
        if (title.Equals("confirm", StringComparison.OrdinalIgnoreCase)
            || title.Equals("reject", StringComparison.OrdinalIgnoreCase))
            return "confirm | reject";
        if (title.Equals("help", StringComparison.OrdinalIgnoreCase))
            return "help";
        if (title.Equals("start", StringComparison.OrdinalIgnoreCase))
            return "start | start <title> — explicit wall Start (not auto)";
        if (title.Equals("shipped", StringComparison.OrdinalIgnoreCase)
            || title.Equals("completed", StringComparison.OrdinalIgnoreCase))
            return "shipped | completed — wall Completed after ship";
        if (title.Equals("events", StringComparison.OrdinalIgnoreCase))
            return "events — list stage cycle event pointers";
        if (title.Equals("note", StringComparison.OrdinalIgnoreCase))
            return "note <text> — append pointer while clock open";
        if (title.Equals("review", StringComparison.OrdinalIgnoreCase)
            || title.Equals("reviews", StringComparison.OrdinalIgnoreCase)
            || title.Equals("remark", StringComparison.OrdinalIgnoreCase)
            || title.Equals("remarks", StringComparison.OrdinalIgnoreCase)
            || title.Equals("rr", StringComparison.OrdinalIgnoreCase))
            return "review <text> | review list | review ack <id> — operator remarks; dig before done (alias remark|rr)";
        if (title.Equals("criteria", StringComparison.OrdinalIgnoreCase))
            return "criteria [dor|ac|dod] — list work-unit criteria";
        if (title.Equals("criterion", StringComparison.OrdinalIgnoreCase))
            return "criterion dor|ac|dod <text> [@manual|@auto|@hybrid] | criterion met|drop <id>";
        if (title.Equals("change_plan", StringComparison.OrdinalIgnoreCase)
            || title.Equals("changeplan", StringComparison.OrdinalIgnoreCase)
            || title.Equals("cp", StringComparison.OrdinalIgnoreCase))
            return "change_plan seed|anchor <a>|check|ack — hybrid DoR blast-radius producer";
        if (title.Equals("leftover", StringComparison.OrdinalIgnoreCase)
            || title.Equals("sweep", StringComparison.OrdinalIgnoreCase)
            || title.Equals("leftovers", StringComparison.OrdinalIgnoreCase))
            return "leftover | leftover apply — close parked/deferred when all AC+DoD met";
        if (title.Equals("start_phase", StringComparison.OrdinalIgnoreCase)
            || title.Equals("phase_start", StringComparison.OrdinalIgnoreCase))
            return "start_phase [act] — wall phase segment begin (re-entry OK)";
        if (title.Equals("complete_phase", StringComparison.OrdinalIgnoreCase)
            || title.Equals("phase_complete", StringComparison.OrdinalIgnoreCase)
            || title.Equals("end_phase", StringComparison.OrdinalIgnoreCase))
            return "complete_phase [act] — wall phase segment end";
        if (title.Equals("feature", StringComparison.OrdinalIgnoreCase)
            || title.Equals("intent", StringComparison.OrdinalIgnoreCase))
            return "feature <name>";
        if (title.Equals("task", StringComparison.OrdinalIgnoreCase)
            || title.Equals("add", StringComparison.OrdinalIgnoreCase)
            || title.Equals("stage", StringComparison.OrdinalIgnoreCase))
            return "task <title> @act";
        _ = kind;
        return null;
    }
}
