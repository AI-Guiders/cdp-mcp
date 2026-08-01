#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>CCL verbs peeled from Apply (soft-warn). null = not handled.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryBoard(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
        if (head is "feature" or "intent")
        {
            if (tokens.Count < 2)
                return (merged, Err("feature needs name", "feature desk-comfort | feature focus Y | feature Y @focus"));

            // feature focus <name> → feature_focus (not title "focus <name>")
            if (tokens[1].Equals("focus", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Count < 3)
                    return (merged, Err("feature focus needs name", "feature focus desk-comfort"));
                var (focusTitle, _) = SplitTitlePhase(tokens.Skip(2).ToList());
                if (focusTitle.Length == 0)
                    return (merged, Err("feature focus needs name", "feature focus desk-comfort"));
                if (IsBoardListAlias(focusTitle))
                    return (merged, Err($"'{focusTitle}' is a REPL verb — not a feature title", "feature focus <name>"));
                if (ReservedTitleHint(focusTitle, kind: "feature") is { } focusHint)
                    return (merged, Err($"'{focusTitle}' is a REPL verb — not a feature title", focusHint));
                merged["go"] = JsonSerializer.SerializeToElement("plan");
                merged["go_args"] = JsonSerializer.SerializeToElement(new { title = focusTitle, op = "feature_focus" });
                merged["tm_op"] = JsonSerializer.SerializeToElement("feature_focus");
                return (merged, null);
            }

            var (title, _) = SplitTitlePhase(tokens.Skip(1).ToList());
            if (title.Length == 0)
                return (merged, Err("feature needs name", "feature desk-comfort | feature focus Y | feature Y @focus"));
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

        // Explicit note pointer while clock open (text= body only — never title=).
        if (head is "note")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("note");
            var text = tokens.Count >= 2 ? string.Join(' ', tokens.Skip(1)) : "";
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "note", text });
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
        return null;
    }
}
