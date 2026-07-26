#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Cockpit Command Line (CCL) — desk <c>cmd=</c> steers seats / soft organs (ADR 0138 / 0191 / 0193).
/// Examples: <c>go browser</c>, <c>layout agent</c>, <c>probe</c>, <c>report</c>, <c>feature X</c>.
/// </summary>
internal static class IdeRepl
{
    public const string SchemaVersion = "ccl/v1";

    /// <summary>
    /// Merge parsed line into cockpit args. Returns help/error object when not a steer.
    /// </summary>
    public static (Dictionary<string, JsonElement> Args, object? Direct)? Apply(
        string line,
        IReadOnlyDictionary<string, JsonElement> cockpitArgs)
    {
        var raw = (line ?? "").Trim();
        if (raw.Length == 0)
            return (new Dictionary<string, JsonElement>(cockpitArgs, StringComparer.Ordinal), Help("empty"));

        // Strip leading prompt noise
        if (raw.StartsWith('>') || raw.StartsWith('$') || raw.StartsWith(':'))
            raw = raw[1..].Trim();

        var merged = new Dictionary<string, JsonElement>(cockpitArgs, StringComparer.Ordinal);
        // Consume the cmd line so we don't re-parse.
        merged.Remove("cmd");
        merged.Remove("line");
        merged.Remove("repl");
        merged.Remove("ccl");
        merged.Remove("ccc");

        var tokens = Tokenize(raw);
        if (tokens.Count == 0)
            return (merged, Help("empty"));

        var head = tokens[0].ToLowerInvariant();

        if (head is "help" or "?" or "h" or "ccc")
            return (merged, Help(null));

        if (head is "clear" or "seat_clear" or "reset")
        {
            merged["pin_clear"] = JsonSerializer.SerializeToElement(true);
            return (merged, null);
        }

        if (head is "layout" or "preset" or "desk")
        {
            if (tokens.Count < 2)
                return (merged, Err("layout needs id", "layout agent | layout cockpit | layout code+net"));
            merged["layout"] = JsonSerializer.SerializeToElement(tokens[1]);
            return (merged, null);
        }

        if (head is "seat")
        {
            if (tokens.Count < 3)
                return (merged, Err("seat needs seat + organ", "seat m git | seat forward editor"));
            merged["seat"] = JsonSerializer.SerializeToElement(tokens[1]);
            merged["organ"] = JsonSerializer.SerializeToElement(tokens[2]);
            return (merged, null);
        }

        if (head is "go" or "do" or "open")
        {
            if (tokens.Count < 2)
                return (merged, Err("go needs organ", "go browser | go editor | go report | go plan"));
            ApplyGo(merged, tokens, start: 1);
            return (merged, null);
        }

        if (head is "mfd" or "page")
        {
            if (tokens.Count < 2)
                return (merged, Err("mfd needs alias", "mfd nav | mfd chk → prefer go=sys|chk"));
            merged["mfd"] = JsonSerializer.SerializeToElement(tokens[1]);
            return (merged, null);
        }

        // Probe channel → script organ (ADR 0193).
        if (head is "probe" or "script")
        {
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "check" or "compile")
                {
                    merged["go"] = JsonSerializer.SerializeToElement("script_check");
                    if (tokens.Count >= 3)
                        ApplyGoArgsOnly(merged, tokens, start: 2);
                    return (merged, null);
                }

                if (sub is "run" or "dry_run" or "dryrun")
                {
                    merged["go"] = JsonSerializer.SerializeToElement("script_run");
                    if (tokens.Count >= 3)
                        ApplyGoArgsOnly(merged, tokens, start: 2);
                    return (merged, null);
                }

                if (sub is "last" or "report")
                {
                    merged["go"] = JsonSerializer.SerializeToElement("report");
                    return (merged, null);
                }

                if (sub is "open" or "put" or "new")
                {
                    merged["go"] = JsonSerializer.SerializeToElement(sub is "open" ? "script_open" : "script_put");
                    if (tokens.Count >= 3)
                        ApplyGoArgsOnly(merged, tokens, start: 2);
                    return (merged, null);
                }

                // probe <path> → open
                merged["go"] = JsonSerializer.SerializeToElement("script_open");
                ApplyGoArgsOnly(merged, tokens, start: 1);
                return (merged, null);
            }

            merged["go"] = JsonSerializer.SerializeToElement("script_scene");
            return (merged, null);
        }

        if (head is "check" or "compile")
        {
            merged["go"] = JsonSerializer.SerializeToElement("script_check");
            if (tokens.Count >= 2)
                ApplyGoArgsOnly(merged, tokens, start: 1);
            return (merged, null);
        }

        if (head is "run")
        {
            merged["go"] = JsonSerializer.SerializeToElement("script_run");
            if (tokens.Count >= 2)
                ApplyGoArgsOnly(merged, tokens, start: 1);
            return (merged, null);
        }

        if (head is "report" or "evidence" or "pfd")
        {
            merged["go"] = JsonSerializer.SerializeToElement("report");
            return (merged, null);
        }

        if (head is "alert" or "eicas")
        {
            merged["go"] = JsonSerializer.SerializeToElement("alert");
            return (merged, null);
        }

        if (head is "sys")
        {
            merged["go"] = JsonSerializer.SerializeToElement("sys");
            return (merged, null);
        }

        if (head is "chk")
        {
            merged["go"] = JsonSerializer.SerializeToElement("chk");
            return (merged, null);
        }

        if (head is "nav")
        {
            merged["go"] = JsonSerializer.SerializeToElement("nav");
            return (merged, null);
        }

        if (head is "gates" or "quality")
        {
            merged["go"] = JsonSerializer.SerializeToElement("quality");
            return (merged, null);
        }

        if (head is "feature" or "intent")
        {
            if (tokens.Count < 2)
                return (merged, Err("feature needs name", "feature desk-comfort"));
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            var title = string.Join(' ', tokens.Skip(1));
            merged["go_args"] = JsonSerializer.SerializeToElement(new { title, op = "feature" });
            merged["tm_op"] = JsonSerializer.SerializeToElement("feature");
            return (merged, null);
        }

        if (head is "task" or "add")
        {
            // Nested: task under <parentTitle> <childTitle…>
            if (tokens.Count >= 4
                && tokens[1].Equals("under", StringComparison.OrdinalIgnoreCase))
            {
                var parent = tokens[2];
                var title = string.Join(' ', tokens.Skip(3));
                if (title.Length == 0)
                    return (merged, Err("task under needs child title", "task under omit-tiles ship-omit"));
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, under = parent, op = "task" });
                merged["tm_op"] = JsonSerializer.SerializeToElement("task");
                return (merged, null);
            }

            if (tokens.Count < 2)
                return (merged, Err("task needs title", "task omit-tiles | task under parent child"));
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            var taskTitle = string.Join(' ', tokens.Skip(1));
            merged["go_args"] = JsonSerializer.SerializeToElement(new { title = taskTitle, op = "task" });
            merged["tm_op"] = JsonSerializer.SerializeToElement("task");
            return (merged, null);
        }

        if (head is "focus")
        {
            if (tokens.Count < 2)
                return (merged, Err("focus needs task", "focus omit-tiles"));
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            var title = string.Join(' ', tokens.Skip(1));
            merged["go_args"] = JsonSerializer.SerializeToElement(new { title, op = "focus" });
            merged["tm_op"] = JsonSerializer.SerializeToElement("focus");
            return (merged, null);
        }

        if (head is "done" or "complete")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("done");
            if (tokens.Count >= 2)
            {
                var title = string.Join(' ', tokens.Skip(1));
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, op = "done" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "done" });
            return (merged, null);
        }

        if (head is "tasks" or "plan" or "board")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("board");
            return (merged, null);
        }

        if (head is "park")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("park");
            if (tokens.Count >= 2)
            {
                var title = string.Join(' ', tokens.Skip(1));
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, op = "park" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "park" });
            return (merged, null);
        }

        if (head is "deploy" or "hard_deploy" or "soft_deploy")
        {
            merged["go"] = JsonSerializer.SerializeToElement(
                head is "soft_deploy" ? "soft_deploy" : head is "hard_deploy" ? "hard_deploy" : "deploy");
            var mode = head is "soft_deploy" ? "soft" : "hard";
            string? target = null;
            var dry = false;
            for (var i = 1; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.StartsWith("target=", StringComparison.OrdinalIgnoreCase))
                {
                    target = t["target=".Length..];
                    continue;
                }

                if (t.Equals("target", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    target = tokens[++i];
                    continue;
                }

                if (t is "soft")
                {
                    mode = "soft";
                    continue;
                }

                if (t is "hard")
                {
                    mode = "hard";
                    continue;
                }

                if (t is "dry" or "dry_run" or "peek")
                {
                    dry = true;
                    continue;
                }

                if (t is "sibling" or "self" or "release" or "debug")
                {
                    target ??= t;
                    continue;
                }
            }

            merged["go_args"] = JsonSerializer.SerializeToElement(new
            {
                mode,
                target,
                dry_run = dry ? true : (bool?)null
            });
            return (merged, null);
        }

        if (head is "promote" or "promote_plan" or "ask")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("promote");
            if (tokens.Count >= 2)
            {
                var notes = string.Join(' ', tokens.Skip(1));
                merged["go_args"] = JsonSerializer.SerializeToElement(new { notes, op = "promote" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "promote" });
            return (merged, null);
        }

        if (head is "share")
        {
            var with = "operator";
            string? what = null;
            string? ask = null;
            var notesParts = new List<string>();
            for (var i = 1; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.StartsWith("with=", StringComparison.OrdinalIgnoreCase))
                {
                    with = t["with=".Length..];
                    continue;
                }

                if (t.Equals("with", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    with = tokens[++i];
                    continue;
                }

                if (t.StartsWith("what=", StringComparison.OrdinalIgnoreCase))
                {
                    what = t["what=".Length..];
                    continue;
                }

                if (t.Equals("what", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    what = tokens[++i];
                    continue;
                }

                if (t.StartsWith("ask=", StringComparison.OrdinalIgnoreCase))
                {
                    ask = t["ask=".Length..];
                    continue;
                }

                if (t.Equals("ask", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    ask = tokens[++i];
                    continue;
                }

                if (t is "plan" or "buffer")
                {
                    what ??= t;
                    continue;
                }

                if (t is "operator" or "human" or "user" or "me")
                {
                    with = t;
                    continue;
                }

                notesParts.Add(t);
            }

            what ??= "buffer";
            var notes = notesParts.Count > 0 ? string.Join(' ', notesParts) : null;
            if (what.Equals("plan", StringComparison.OrdinalIgnoreCase))
            {
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["tm_op"] = JsonSerializer.SerializeToElement("share");
                merged["go_args"] = JsonSerializer.SerializeToElement(new
                {
                    op = "share",
                    with,
                    what = "plan",
                    ask = ask ?? "confirm",
                    notes
                });
                return (merged, null);
            }

            merged["go"] = JsonSerializer.SerializeToElement("share");
            merged["go_args"] = JsonSerializer.SerializeToElement(new
            {
                with,
                what = "buffer",
                ask = ask ?? "none",
                notes
            });
            return (merged, null);
        }

        if (head is "confirm" or "approved")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("confirm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "confirm" });
            return (merged, null);
        }

        if (head is "reject" or "denied")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("reject");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "reject" });
            return (merged, null);
        }

        if (head is "drop" or "rm" or "delete")
        {
            // drop feature X | drop task X | drop X | drop
            if (tokens.Count >= 3
                && tokens[1] is "feature" or "intent" or "task" or "stage")
            {
                var kind = tokens[1] is "feature" or "intent" ? "feature" : "task";
                var title = string.Join(' ', tokens.Skip(2));
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["tm_op"] = JsonSerializer.SerializeToElement(kind == "feature" ? "feature_drop" : "task_drop");
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, kind, op = "drop" });
                return (merged, null);
            }

            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("drop");
            if (tokens.Count >= 2)
            {
                var title = string.Join(' ', tokens.Skip(1));
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, op = "drop" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "drop" });
            return (merged, null);
        }

        if (head is "full" or "pane_full")
        {
            if (tokens.Count < 2)
                return (merged, Err("full needs pin", "full browser | full report"));
            merged["pane_full"] = JsonSerializer.SerializeToElement(tokens[1]);
            return (merged, null);
        }

        // Bare organ: `browser`, `git`, `editor` …
        if (IdeCockpit.IsKnownGoVerb(tokens[0]) || IdeCockpit.IsKnownPinAlias(tokens[0]))
        {
            ApplyGo(merged, tokens, start: 0);
            return (merged, null);
        }

        // `p project` / `m browser` / `forward editor` — seat shorthand
        if (IdeDeskSeats.NormalizeSeatId(head) is { } seatId)
        {
            if (tokens.Count < 2)
                return (merged, Err($"{seatId} needs organ", $"{seatId} browser"));
            merged["seat"] = JsonSerializer.SerializeToElement(seatId);
            merged["organ"] = JsonSerializer.SerializeToElement(tokens[1]);
            return (merged, null);
        }

        return (merged, Err("unknown_cmd", "help | go report | probe | check | run | layout agent | plan"));
    }

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
                "sys",
                "chk",
                "nav",
                "gates",
                "go report",
                "go alert",
                "full report",
                "feature desk-comfort",
                "task ship-omit",
                "promote",
                "share",
                "share with operator",
                "share plan",
                "deploy",
                "deploy dry",
                "confirm",
                "reject",
                "plan",
                "go browser",
                "seat m git",
                "clear",
            },
        hint = "CCL (cmd=). Channels: sit/plan · work/editor · probe/script · report · alert · sys/chk. CCC=help."
    };

    static object Err(string error, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        role = "ccl",
        error,
        hint
    };
}
