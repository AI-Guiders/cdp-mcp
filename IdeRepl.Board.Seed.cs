#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>feature/intent + task/add seed verbs.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryBoardSeed(
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

        return null;
    }
}
