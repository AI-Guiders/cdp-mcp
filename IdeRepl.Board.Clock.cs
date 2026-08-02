#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>product/phase/focus + wall-clock lifecycle verbs.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryBoardClock(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
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

        return null;
    }
}
