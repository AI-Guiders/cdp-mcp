#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    static void ApplyGoArgsOnly(Dictionary<string, JsonElement> merged, IReadOnlyList<string> tokens, int start)
    {
        if (tokens.Count <= start)
            return;

        var ga = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (merged.TryGetValue("go_args", out var existing) && existing.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in existing.EnumerateObject())
                ga[p.Name] = p.Value.Clone();
        }

        for (var i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var eq = t.IndexOf('=');
            if (eq > 0)
            {
                ga[t[..eq]] = JsonSerializer.SerializeToElement(t[(eq + 1)..]);
                continue;
            }

            if (t.Contains("://", StringComparison.Ordinal) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                ga["url"] = JsonSerializer.SerializeToElement(t);
            else if (t.Contains('\\') || t.Contains('/') || t.Contains('.'))
                ga["path"] = JsonSerializer.SerializeToElement(t);
            else if (!ga.ContainsKey("q"))
                ga["q"] = JsonSerializer.SerializeToElement(t);
            else
                ga[$"arg{i}"] = JsonSerializer.SerializeToElement(t);
        }

        merged["go_args"] = JsonSerializer.SerializeToElement(ga);
    }

    static void ApplyGo(Dictionary<string, JsonElement> merged, IReadOnlyList<string> tokens, int start)
    {
        merged["go"] = JsonSerializer.SerializeToElement(tokens[start]);
        if (tokens.Count <= start + 1)
            return;

        ApplyGoArgsOnly(merged, tokens, start: start + 1);
    }

    static List<string> Tokenize(string line)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        char quote = '\0';
        foreach (var ch in line)
        {
            if (inQuote)
            {
                if (ch == quote) { inQuote = false; continue; }
                sb.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuote = true;
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }

                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
            list.Add(sb.ToString());
        return list;
    }

    static object Help(string? note) => new
    {
        ok = true,
        schema = SchemaVersion,
        role = "ccl_help",
        note,
        alias = "ccc",
        examples =
            new[]
            {
                "layout agent",
                "probe",
                "check",
                "run",
                "report",
                "alert",
                "sa",
                "problems",
                "problems 1",
                "plugins",
                "plugins search plantuml",
                "plugins want plantuml",
                "plugins install jebbs.plantuml",
                "plugins groups",
                "plugins disable group diagrams",
                "plugins group add jebbs.plantuml work",
                "plugins preview",
                "sys",
                "chk",
                "ecl",
                "qrh",
                "qrh open dap-pdb-lock",
                "qrh search pdb",
                "eqrh",
                "review",
                "review files",
                "nav",
                "gates",
                "go report",
                "go alert",
                "full report",
                "feature desk-comfort",
                "task ship-omit @act",
                "phase act",
                "promote",
                "share",
                "share with operator",
                "share plan",
                "share report",
                "deploy",
                "deploy dry",
                "confirm",
                "reject",
                "plan",
                "go browser",
                "seat m git",
                "clear",
            },
        hint = "CCL (cmd=). Channels: sit/plan · work/editor · probe/script · report · alert · sys/ecl/qrh/review. CCC=help."
    };

    /// <summary>Split trailing <c>@act</c> / TM directive and optional <c>#CDP</c> product tag from title tokens.</summary>
    internal static (string Title, string? Phase, string? Product) SplitTitleMeta(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return ("", null, null);

        var list = tokens.ToList();
        string? product = null;
        // Peel trailing #Product tags (order-flexible with @phase).
        for (var guard = 0; guard < 4 && list.Count > 0; guard++)
        {
            var last = list[^1];
            if (last.StartsWith('#') && last.Length > 1 && product is null)
            {
                product = last[1..];
                list.RemoveAt(list.Count - 1);
                continue;
            }

            break;
        }

        var (title, phase) = SplitTitlePhase(list);
        // Also allow #Product before @phase: peeled above only from end — peel leftover # from title tokens once more after phase.
        if (product is null && list.Count > 0)
        {
            // If phase was peeled, list is already reduced inside SplitTitlePhase via return — re-check title words.
            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (words.Count > 0 && words[^1].StartsWith('#') && words[^1].Length > 1)
            {
                product = words[^1][1..];
                words.RemoveAt(words.Count - 1);
                title = string.Join(' ', words);
            }
        }

        return (title, phase, product);
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

    /// <summary>
    /// <c>plugins disable group javascript</c> | <c>plugins enable g1</c> | <c>plugins disable jebbs.plantuml</c>
    /// </summary>
    static object ParsePluginsEnableDisable(IReadOnlyList<string> tokens, string sub)
    {
        var op = sub is "on" or "enable" ? "enable" : "disable";
        if (tokens.Count < 3)
            return new { op };

        if (tokens[2].Equals("group", StringComparison.OrdinalIgnoreCase)
            || tokens[2].Equals("grp", StringComparison.OrdinalIgnoreCase))
        {
            var group = tokens.Count >= 4 ? tokens[3] : "";
            return new { op, group };
        }

        var target = tokens[2];
        if (target.StartsWith('g') && int.TryParse(target.AsSpan(1), out _))
            return new { op, row = target };
        return new { op, id = target };
    }

    /// <summary><c>plugins group add jebbs.plantuml work</c> | <c>plugins group remove …</c></summary>
    static object ParsePluginsGroup(IReadOnlyList<string> tokens)
    {
        // plugins group add|remove <id> <group>
        if (tokens.Count < 5)
            return new { op = "group" };
        var sub = tokens[2].ToLowerInvariant();
        if (sub is not ("add" or "remove" or "rm" or "del"))
            return new { op = "group", id = tokens[2], group = tokens[3], sub = "add" };
        return new { op = "group", sub, id = tokens[3], group = tokens[4] };
    }

    /// <summary>
    /// <c>plugins install jebbs.plantuml</c> | <c>… id version</c> | <c>… path.vsix</c> | <c>… s1</c>
    /// </summary>
    static object ParsePluginsInstall(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 3)
            return new { op = "install" };

        var target = tokens[2];
        var version = tokens.Count >= 4 ? tokens[3] : null;

        // row from last search
        if (target.StartsWith('s') && int.TryParse(target.AsSpan(1), out _))
            return version is { Length: > 0 }
                ? new { op = "install", row = target, version }
                : new { op = "install", row = target };

        // local path / vsix
        if (LooksLikeLocalPluginPath(target))
            return new { op = "install", path = target };

        // Open VSX id
        return version is { Length: > 0 }
            ? new { op = "install", id = target, version }
            : new { op = "install", id = target };
    }

    static bool LooksLikeLocalPluginPath(string target)
    {
        if (target.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase))
            return true;
        if (target.Contains('/') || target.Contains('\\') || target.Contains(':'))
            return true;
        try
        {
            if (File.Exists(target) || Directory.Exists(target))
                return true;
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    static object Err(string error, string hint) => new { ok = false, schema = SchemaVersion, role = "ccl", error, hint };
}
