#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>CCL verbs peeled from Apply (soft-warn). null = not handled.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryOps(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
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

        if (head is "halt" or "stop_world")
        {
            merged["go"] = JsonSerializer.SerializeToElement("ignite_desk");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "halt" });
            return (merged, null);
        }

        if (head is "await_partner" or "await_operator" or "await" or "epic_closed")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("await_operator");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "await_partner" });
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

        return null;
    }
}
