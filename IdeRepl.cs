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

        if (head is "alert" or "eicas" or "sa")
        {
            merged["go"] = JsonSerializer.SerializeToElement("alert");
            return (merged, null);
        }

        if (head is "problems" or "problem" or "errlist" or "errorlist" or "err" or "diags")
        {
            merged["go"] = JsonSerializer.SerializeToElement("problems");
            if (tokens.Count >= 2)
            {
                var pick = tokens[1];
                merged["go_args"] = JsonSerializer.SerializeToElement(new { row = pick, aim = true });
            }
            return (merged, null);
        }

        if (head is "plugins" or "plugin" or "vsix")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plugins");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "search" or "find" or "query")
                {
                    var q = tokens.Count >= 3 ? string.Join(' ', tokens.Skip(2)) : "";
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "search", q });
                }
                else if (sub is "install" or "add")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(ParsePluginsInstall(tokens));
                }
                else if (sub is "want" or "need" or "get")
                {
                    var q = tokens.Count >= 3 ? string.Join(' ', tokens.Skip(2)) : "";
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "want", q });
                }
                else if (sub is "preview" or "render" or "png")
                {
                    var path = tokens.Count >= 3 ? tokens[2] : null;
                    merged["go_args"] = path is { Length: > 0 }
                        ? JsonSerializer.SerializeToElement(new { op = "preview", path })
                        : JsonSerializer.SerializeToElement(new { op = "preview" });
                }
                else if (sub is "list" or "installed")
                {
                    var all = tokens.Count >= 3 && tokens[2].Equals("all", StringComparison.OrdinalIgnoreCase);
                    merged["go_args"] = all
                        ? JsonSerializer.SerializeToElement(new { op = "list", all = true })
                        : JsonSerializer.SerializeToElement(new { op = "list" });
                }
                else if (sub is "reharvest" or "rescan" or "reclassify")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "reharvest" });
                }
                else if (sub is "groups" or "grouplist")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "groups" });
                }
                else if (sub is "enable" or "on" or "disable" or "off")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(ParsePluginsEnableDisable(tokens, sub));
                }
                else if (sub is "group" or "tag")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(ParsePluginsGroup(tokens));
                }
                else
                {
                    // "plugins s1" → install from last search; "plugins plantuml" → search
                    if (tokens[1].StartsWith('s') && int.TryParse(tokens[1].AsSpan(1), out _))
                    {
                        merged["go_args"] = JsonSerializer.SerializeToElement(
                            new { op = "install", row = tokens[1] });
                    }
                    else if (tokens[1].StartsWith('g') && int.TryParse(tokens[1].AsSpan(1), out _)
                             || int.TryParse(tokens[1], out _))
                    {
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { row = tokens[1] });
                    }
                    else
                    {
                        var q = string.Join(' ', tokens.Skip(1));
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "search", q });
                    }
                }
            }
            return (merged, null);
        }

        if (head is "sys")
        {
            merged["go"] = JsonSerializer.SerializeToElement("sys");
            return (merged, null);
        }

        if (head is "chk" or "ecl")
        {
            merged["go"] = JsonSerializer.SerializeToElement("ecl");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "list" or "catalog")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "list" });
                }
                else if (sub is "reset")
                {
                    var what = tokens.Count >= 3 ? tokens[2] : "overlay";
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "reset", what });
                }
                else if (sub is "ack" or "done" or "unack")
                {
                    if (tokens.Count < 4)
                        return (merged, Err("ecl ack needs checklist+item", "ecl ack ship push"));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new
                    {
                        op = sub == "done" ? "ack" : sub,
                        checklist = tokens[2],
                        item = tokens[3]
                    });
                }
                else if (sub is "add")
                {
                    // ecl add id=foo title=Bar link=phase:act  OR  ecl add foo phase:act
                    var id = tokens.Count >= 3 ? tokens[2] : null;
                    string? title = null;
                    string? link = null;
                    for (var i = 2; i < tokens.Count; i++)
                    {
                        var t = tokens[i];
                        if (t.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
                            id = t[3..];
                        else if (t.StartsWith("title=", StringComparison.OrdinalIgnoreCase))
                            title = t[6..];
                        else if (t.StartsWith("link=", StringComparison.OrdinalIgnoreCase)
                                 || t.StartsWith("links=", StringComparison.OrdinalIgnoreCase))
                            link = t[(t.IndexOf('=') + 1)..];
                        else if (i == 2 && id is null)
                            id = t;
                        else if (link is null && t.Contains(':', StringComparison.Ordinal))
                            link = t;
                        else if (title is null && i > 2)
                            title = t;
                    }

                    if (id is null || link is null)
                        return (merged, Err("ecl add needs id+link", "ecl add mine phase:act"));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "add", id, title, link });
                }
                else if (sub is "remove" or "rm" or "enable" or "disable" or "on" or "off")
                {
                    if (tokens.Count < 3)
                        return (merged, Err("ecl needs id", $"ecl {sub} ship"));
                    var op = sub is "on" ? "enable" : sub is "off" ? "disable" : sub is "rm" ? "remove" : sub;
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op, id = tokens[2] });
                }
                else if (sub is "link" or "unlink")
                {
                    if (tokens.Count < 4)
                        return (merged, Err("ecl link needs id+link", "ecl link ship phase:verify"));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new
                    {
                        op = sub,
                        id = tokens[2],
                        link = tokens[3]
                    });
                }
                else
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "run" });
                }
            }

            return (merged, null);
        }

        if (head is "qrh" or "eqrh" or "handbook")
        {
            merged["go"] = JsonSerializer.SerializeToElement("qrh");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "index" or "list" or "catalog")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "index" });
                }
                else if (sub is "search" or "find")
                {
                    if (tokens.Count < 3)
                        return (merged, Err("qrh search needs q", "qrh search pdb"));
                    var q = string.Join(' ', tokens.Skip(2));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "search", q });
                }
                else if (sub is "shelf" or "section")
                {
                    if (tokens.Count < 3)
                        return (merged, Err("qrh shelf needs name", "qrh shelf emergency"));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "shelf", shelf = tokens[2] });
                }
                else if (sub is "related")
                {
                    var id = tokens.Count >= 3 ? tokens[2] : null;
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "related", id });
                }
                else if (sub is "open")
                {
                    var id = tokens.Count >= 3 ? tokens[2] : null;
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "open", id });
                }
                else
                {
                    // Bare page id: qrh dap-pdb-lock
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "open", id = tokens[1] });
                }
            }

            return (merged, null);
        }

        if (head is "review")
        {
            merged["go"] = JsonSerializer.SerializeToElement("review");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "files" or "list" or "index")
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "files" });
                else if (sub is "open")
                {
                    var path = tokens.Count >= 3 ? string.Join(' ', tokens.Skip(2)) : null;
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "open", path });
                }
                else
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "open", path = tokens[1] });
            }

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
                return (merged, Err("feature needs name", "feature desk-comfort | feature Y @focus"));
            var (title, _) = SplitTitlePhase(tokens.Skip(1).ToList());
            if (title.Length == 0)
                return (merged, Err("feature needs name", "feature desk-comfort | feature Y @focus"));
            if (IsBoardListAlias(title))
            {
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["tm_op"] = JsonSerializer.SerializeToElement("board");
                return (merged, null);
            }

            if (ReservedTitleHint(title, kind: "feature") is { } featureHint)
                return (merged, Err($"'{title}' is a REPL verb — not a feature title", featureHint));

            merged["go"] = JsonSerializer.SerializeToElement("plan");
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
                var rest = tokens.Skip(3).ToList();
                var (childTitle, underPhase, underProduct) = SplitTitleMeta(rest);
                if (childTitle.Length == 0)
                    return (merged, Err("task under needs child title", "task under omit-tiles ship-omit @act #CDP"));
                if (IsBoardListAlias(childTitle)
                    || ReservedTitleHint(childTitle, kind: "task") is not null)
                    return (merged, Err($"'{childTitle}' is a REPL verb — not a task title", "task ship-omit @act #CDP"));
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["go_args"] = JsonSerializer.SerializeToElement(new
                {
                    title = childTitle,
                    under = parent,
                    op = "task",
                    phase = underPhase,
                    product = underProduct
                });
                merged["tm_op"] = JsonSerializer.SerializeToElement("task");
                return (merged, null);
            }

            if (tokens.Count < 2)
                return (merged, Err("task needs title", "task omit-tiles | task ship @act"));
            var taskRest = tokens.Skip(1).ToList();
            if (taskRest.Count > 0
                && (taskRest[^1].Equals("@deferred", StringComparison.OrdinalIgnoreCase)
                    || taskRest[^1].Equals("@defer", StringComparison.OrdinalIgnoreCase)
                    || taskRest[^1].Equals("@parked", StringComparison.OrdinalIgnoreCase)
                    || taskRest[^1].Equals("@park", StringComparison.OrdinalIgnoreCase)))
            {
                var seedOp = taskRest[^1].StartsWith("@park", StringComparison.OrdinalIgnoreCase)
                    ? "park"
                    : "defer";
                var (seedTitle, seedPhase) = SplitTitlePhase(taskRest);
                if (seedTitle.Length == 0)
                    return (merged, Err($"{seedOp} needs title",
                        $"{seedOp} AutoIgnition delivery probe | task X @{seedOp}ed"));
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["tm_op"] = JsonSerializer.SerializeToElement(seedOp);
                merged["go_args"] = seedPhase is null
                    ? JsonSerializer.SerializeToElement(new { title = seedTitle, op = seedOp })
                    : JsonSerializer.SerializeToElement(new { title = seedTitle, op = seedOp, phase = seedPhase });
                return (merged, null);
            }

            var (taskTitle, taskPhase, taskProduct) = SplitTitleMeta(taskRest);
            if (taskTitle.Length == 0)
                return (merged, Err("task needs title", "task omit-tiles | task ship @act #CDP"));
            if (IsBoardListAlias(taskTitle))
            {
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["tm_op"] = JsonSerializer.SerializeToElement("board");
                return (merged, null);
            }

            if (ReservedTitleHint(taskTitle, kind: "task") is { } taskHint)
                return (merged, Err($"'{taskTitle}' is a REPL verb — not a task title", taskHint));

            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["go_args"] = JsonSerializer.SerializeToElement(new
            {
                title = taskTitle,
                op = "task",
                phase = taskPhase,
                product = taskProduct
            });
            merged["tm_op"] = JsonSerializer.SerializeToElement("task");
            return (merged, null);
        }

        if (head is "product" or "category")
        {
            if (tokens.Count < 2)
                return (merged, Err("product needs value", "product CDP | category Cursor | product clear"));
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { product = tokens[1], op = "product" });
            merged["tm_op"] = JsonSerializer.SerializeToElement("product");
            return (merged, null);
        }

        if (head is "phase")
        {
            // phase act — set affinity on active task (soft). Session phase: cdp_context.
            if (tokens.Count < 2)
                return (merged, Err("phase needs value", "phase act | phase verify"));
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { phase = tokens[1], op = "phase" });
            merged["tm_op"] = JsonSerializer.SerializeToElement("phase");
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

        // Explicit wall-clock Start — never auto on focus/edit.
        if (head is "start")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("start");
            if (tokens.Count >= 2)
            {
                var title = string.Join(' ', tokens.Skip(1));
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, op = "start" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "start" });
            return (merged, null);
        }

        // Phase wall segment begin — same ledger gate as note (open stage clock).
        // Re-entry OK: act→verify→act yields separate segments, not a merge.
        if (head is "start_phase" or "phase_start")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("start_phase");
            if (tokens.Count >= 2)
            {
                var phase = tokens[1];
                merged["go_args"] = JsonSerializer.SerializeToElement(new { phase, op = "start_phase" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "start_phase" });
            return (merged, null);
        }

        // Phase wall segment end — pairs with start_phase / cdp_context transition.
        if (head is "complete_phase" or "phase_complete" or "end_phase")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("complete_phase");
            if (tokens.Count >= 2)
            {
                var phase = tokens[1];
                merged["go_args"] = JsonSerializer.SerializeToElement(new { phase, op = "complete_phase" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "complete_phase" });
            return (merged, null);
        }

        // Explicit Completed after ship cycle — wall end (not a score).
        if (head is "shipped" or "completed")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("shipped");
            if (tokens.Count >= 2)
            {
                var title = string.Join(' ', tokens.Skip(1));
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title, op = "shipped" });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "shipped" });
            return (merged, null);
        }

        // Stage cycle event ledger — list pointers for open (or closed) clock.
        if (head is "events")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("events");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "events" });
            return (merged, null);
        }

        // Explicit note pointer while clock open.
        if (head is "note")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("note");
            var text = tokens.Count >= 2 ? string.Join(' ', tokens.Skip(1)) : "";
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "note", title = text, text });
            return (merged, null);
        }

        // Work-unit criteria: list / add / status / drop.
        if (head is "criteria")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("criteria");
            var kind = tokens.Count >= 2 ? tokens[1] : null;
            merged["go_args"] = kind is null
                ? JsonSerializer.SerializeToElement(new { op = "criteria" })
                : JsonSerializer.SerializeToElement(new { op = "criteria", kind });
            return (merged, null);
        }

        if (head is "criterion")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            if (tokens.Count < 2)
            {
                merged["tm_op"] = JsonSerializer.SerializeToElement("criteria");
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "criteria" });
                return (merged, null);
            }

            var second = tokens[1].ToLowerInvariant();
            if (second is "met" or "unmet" or "waived" or "pending" or "drop" or "rm" or "delete")
            {
                var idTok = tokens.Count >= 3 ? tokens[2] : "";
                merged["tm_op"] = JsonSerializer.SerializeToElement(
                    second is "drop" or "rm" or "delete" ? "criterion_drop" : $"criterion_{second}");
                merged["go_args"] = JsonSerializer.SerializeToElement(new
                {
                    op = second is "drop" or "rm" or "delete" ? "criterion_drop" : $"criterion_{second}",
                    criterion_id = idTok,
                    id = idTok,
                    status = second is "drop" or "rm" or "delete" ? null : second
                });
                return (merged, null);
            }

            // criterion dor|ac|dod <text> [@manual|@auto|@hybrid]
            var kind = second;
            var rest = tokens.Skip(2).ToList();
            string? mode = null;
            if (rest.Count > 0)
            {
                var last = rest[^1];
                if (last.StartsWith('@'))
                {
                    mode = last.TrimStart('@');
                    rest.RemoveAt(rest.Count - 1);
                }
            }

            var text = string.Join(' ', rest);
            merged["tm_op"] = JsonSerializer.SerializeToElement("criterion");
            merged["go_args"] = mode is null
                ? JsonSerializer.SerializeToElement(new { op = "criterion", action = "add", kind, text })
                : JsonSerializer.SerializeToElement(new { op = "criterion", action = "add", kind, text, mode });
            return (merged, null);
        }

        // Change Planner — first auto/hybrid criteria producer.
        if (head is "change_plan" or "changeplan" or "cp")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("change_plan");
            var action = tokens.Count >= 2 ? tokens[1].ToLowerInvariant() : "scene";
            if (action is "seed" or "open" or "ensure" or "scene" or "pulse" or "status"
                or "check" or "ack" or "manual_ack")
            {
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "change_plan", action, cp_op = action });
                return (merged, null);
            }

            if (action is "anchor" or "add_anchor")
            {
                var anchorText = tokens.Count >= 3 ? string.Join(' ', tokens.Skip(2)) : "";
                merged["go_args"] = JsonSerializer.SerializeToElement(new
                {
                    op = "change_plan",
                    action = "anchor",
                    cp_op = "anchor",
                    anchor = anchorText,
                    text = anchorText
                });
                return (merged, null);
            }

            // Bare "change_plan <something>" → treat rest as anchor text after implicit seed path via planner.
            var restAnchor = string.Join(' ', tokens.Skip(1));
            merged["go_args"] = JsonSerializer.SerializeToElement(new
            {
                op = "change_plan",
                action = "anchor",
                cp_op = "anchor",
                anchor = restAnchor,
                text = restAnchor
            });
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

        // Leftover sweep — parked/deferred with all AC+DoD met → optional done (no focus steal).
        if (head is "leftover" or "sweep" or "leftovers")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("leftover");
            var action = tokens.Count >= 2 ? tokens[1].ToLowerInvariant() : "";
            if (action is "apply" or "commit" or "done" or "close")
            {
                merged["go_args"] = JsonSerializer.SerializeToElement(new
                {
                    op = "leftover",
                    action = "apply",
                    apply = true
                });
            }
            else
            {
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "leftover" });
            }

            return (merged, null);
        }

        if (head is "defer" or "deferred")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("defer");
            if (tokens.Count >= 2)
            {
                var (deferTitle, deferPhase) = SplitTitlePhase(tokens.Skip(1).ToList());
                if (deferTitle.Length == 0)
                    return (merged, Err("defer needs title", "defer AutoIgnition delivery probe"));
                merged["go_args"] = deferPhase is null
                    ? JsonSerializer.SerializeToElement(new { title = deferTitle, op = "defer" })
                    : JsonSerializer.SerializeToElement(new { title = deferTitle, op = "defer", phase = deferPhase });
            }
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "defer" });
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
            string? from = null;
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

                if (t.StartsWith("from=", StringComparison.OrdinalIgnoreCase))
                {
                    from = t["from=".Length..];
                    continue;
                }

                if (t.Equals("from", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    from = tokens[++i];
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

                if (t is "plan" or "buffer" or "report" or "digest" or "status" or "note")
                {
                    what ??= t;
                    continue;
                }

                if (t is "operator" or "human" or "user" or "me")
                {
                    with = t;
                    continue;
                }

                if (t is "self" or "shelf" or "agent" or "stash")
                {
                    with = t;
                    continue;
                }

                if (t is "latest")
                {
                    from ??= t;
                    continue;
                }

                notesParts.Add(t);
            }

            var notes = notesParts.Count > 0 ? string.Join(' ', notesParts) : null;

            // share from=self|latest — pull shelf (fast path via go=share → cdp_buffer)
            if (!string.IsNullOrWhiteSpace(from))
            {
                merged["go"] = JsonSerializer.SerializeToElement("share");
                merged["go_args"] = JsonSerializer.SerializeToElement(new
                {
                    from,
                    depth = "full",
                    notes
                });
                return (merged, null);
            }

            what ??= string.Equals(IdeShare.NormalizeWith(with), IdeShare.WithSelf, StringComparison.Ordinal)
                     && notes is not null
                ? "note"
                : "buffer";

            if (what.Equals("plan", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(IdeShare.NormalizeWith(with), IdeShare.WithSelf, StringComparison.Ordinal))
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

            if (what.Equals("report", StringComparison.OrdinalIgnoreCase)
                || what.Equals("digest", StringComparison.OrdinalIgnoreCase)
                || what.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["tm_op"] = JsonSerializer.SerializeToElement("report");
                merged["go_args"] = JsonSerializer.SerializeToElement(new
                {
                    op = "report",
                    with,
                    what = "report",
                    ask = "none",
                    notes
                });
                return (merged, null);
            }

            // with=self + free text → shelf put (body=notes); else buffer share
            if (string.Equals(IdeShare.NormalizeWith(with), IdeShare.WithSelf, StringComparison.Ordinal)
                && notes is not null)
            {
                merged["go"] = JsonSerializer.SerializeToElement("share");
                merged["go_args"] = JsonSerializer.SerializeToElement(new
                {
                    with = "self",
                    what,
                    body = notes,
                    ask = "none"
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

        if (head is "confirm" or "approved" or "cleared")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "approved" });
            return (merged, null);
        }

        if (head is "reject" or "denied" or "go_around" or "goaround")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "go_around" });
            return (merged, null);
        }

        if (head is "stabilized" or "stable")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "stabilized" });
            return (merged, null);
        }

        if (head is "hold" or "standby")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "hold" });
            return (merged, null);
        }

        if (head is "unable")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "unable" });
            return (merged, null);
        }

        if (head is "negative")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "negative" });
            return (merged, null);
        }

        if (head is "say_again" or "sayagain")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "say_again" });
            return (merged, null);
        }

        if (head is "roger" or "wilco" or "continue")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = head });
            return (merged, null);
        }

        // "go around" two-token
        if (head is "go" && tokens.Count >= 2 && tokens[1] is "around")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code = "go_around" });
            return (merged, null);
        }

        if (head is "crm" or "callout")
        {
            merged["go"] = JsonSerializer.SerializeToElement("crm");
            if (tokens.Count >= 2)
            {
                var sub = string.Join('_', tokens.Skip(1)).ToLowerInvariant();
                if (sub is "scene" or "last" or "clear" or "lexicon" or "call")
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = sub });
                else
                {
                    var code = IdeCrmChannel.NormCode(sub);
                    if (code is not null)
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "respond", code });
                }
            }
            return (merged, null);
        }

        if (head is "files" or "files_desk" or "explorer" or "fm" or "ls" or "dir")
        {
            merged["go"] = JsonSerializer.SerializeToElement("files_desk");
            if (head is "ls" or "dir")
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "list" });
            else if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "scene" or "list" or "ls" or "up" or "tree" or "roots" or "clear" or "stat" or "open" or "search" or "cd")
                {
                    var op = sub is "ls" ? "list" : sub;
                    if (tokens.Count >= 3)
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op, path = string.Join(' ', tokens.Skip(2)) });
                    else
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op });
                }
                else
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "cd", path = string.Join(' ', tokens.Skip(1)) });
            }
            return (merged, null);
        }

        if (head is "cd" && tokens.Count >= 2)
        {
            merged["go"] = JsonSerializer.SerializeToElement("files_desk");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "cd", path = string.Join(' ', tokens.Skip(1)) });
            return (merged, null);
        }

        if (head is "ignite" or "ignite_desk" or "autoignite" or "cdt_ignite" or "cdp_ignite")
        {
            merged["go"] = JsonSerializer.SerializeToElement("ignite_desk");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "scene" or "probe" or "chats" or "list" or "arms" or "disarm")
                {
                    if (sub is "disarm" && tokens.Count >= 3)
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "disarm", id = tokens[2] });
                    else if (sub is "arms")
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "list" });
                    else
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = sub });
                }
                else if (sub is "arm" or "send" or "fire")
                {
                    var rest = string.Join(' ', tokens.Skip(2));
                    if (sub is "arm")
                    {
                        // arm build_finished … | arm 5m … | arm timer 5m …
                        var when = "timer";
                        var inRaw = (string?)null;
                        var msgStart = 2;
                        if (tokens.Count >= 3)
                        {
                            var t2 = tokens[2].ToLowerInvariant();
                            if (t2 is "build" or "build_finished" or "test" or "test_finished" or "timer")
                            {
                                when = IdeIgniteArmHost.NormalizeEvent(t2);
                                msgStart = 3;
                                if (when == "timer" && tokens.Count >= 4
                                    && IdeIgniteArmHost.TryParseDuration(tokens[3], out _))
                                {
                                    inRaw = tokens[3];
                                    msgStart = 4;
                                }
                            }
                            else if (IdeIgniteArmHost.TryParseDuration(t2, out _))
                            {
                                when = "timer";
                                inRaw = t2;
                                msgStart = 3;
                            }
                        }

                        var body = string.Join(' ', tokens.Skip(msgStart));
                        merged["go_args"] = JsonSerializer.SerializeToElement(new
                        {
                            op = "arm",
                            when,
                            @in = inRaw,
                            message = string.IsNullOrWhiteSpace(body) ? null : body,
                            task = string.IsNullOrWhiteSpace(body) ? null : body
                        });
                    }
                    else if (!string.IsNullOrWhiteSpace(rest))
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "send", message = rest });
                    else
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "send" });
                }
                else
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "send", message = string.Join(' ', tokens.Skip(1)) });
            }
            return (merged, null);
        }

        if (head is "arm")
        {
            // shorthand: arm 5m do X | arm build_finished do X
            merged["go"] = JsonSerializer.SerializeToElement("ignite_desk");
            var when = "timer";
            var inRaw = (string?)null;
            var msgStart = 1;
            if (tokens.Count >= 2)
            {
                var t1 = tokens[1].ToLowerInvariant();
                if (t1 is "build" or "build_finished" or "test" or "test_finished" or "timer")
                {
                    when = IdeIgniteArmHost.NormalizeEvent(t1);
                    msgStart = 2;
                    if (when == "timer" && tokens.Count >= 3 && IdeIgniteArmHost.TryParseDuration(tokens[2], out _))
                    {
                        inRaw = tokens[2];
                        msgStart = 3;
                    }
                }
                else if (IdeIgniteArmHost.TryParseDuration(t1, out _))
                {
                    inRaw = t1;
                    msgStart = 2;
                }
            }

            var body = string.Join(' ', tokens.Skip(msgStart));
            merged["go_args"] = JsonSerializer.SerializeToElement(new
            {
                op = "arm",
                when,
                @in = inRaw,
                message = string.IsNullOrWhiteSpace(body) ? null : body,
                task = string.IsNullOrWhiteSpace(body) ? null : body
            });
            return (merged, null);
        }

        if (head is "disarm")
        {
            merged["go"] = JsonSerializer.SerializeToElement("ignite_desk");
            if (tokens.Count >= 2 && tokens[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "disarm", all = true });
            else if (tokens.Count >= 2)
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "disarm", id = tokens[1] });
            else
                merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "list" });
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

    static object Err(string error, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        role = "ccl",
        error,
        hint
    };
}
