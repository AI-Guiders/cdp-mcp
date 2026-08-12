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

        if (head is "executor" or "assignee")
        {
            if (tokens.Count < 2)
                return (merged, Err("executor needs value", "executor Sierra | assignee Кир | executor clear"));
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { executor = tokens[1], op = "executor" });
            merged["tm_op"] = JsonSerializer.SerializeToElement("executor");
            return (merged, null);
        }

        if (head is "lane" or "focus_lane" or "who_lane")
        {
            if (tokens.Count < 2)
                return (merged, Err("lane needs Who", "lane Кир | lane Sierra | lane clear"));
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["go_args"] = JsonSerializer.SerializeToElement(new { lane = tokens[1], op = "lane" });
            merged["tm_op"] = JsonSerializer.SerializeToElement("lane");
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
            // Merge — do not wipe cockpit go_args.evidence / force (human_face_cide_shot).
            // Lived 2026-08-06: inline dig=/evidence=/domain= were swallowed as title → task not found.
            MergeClockDoneShipArgs(tokens, skip: 1, merged, op: "done");
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
            MergeClockDoneShipArgs(tokens, skip: 1, merged, op: "shipped");
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

        // Operator Review Results — durable remarks on leaf (dialog → stamp; dig before done).
        // SoftInstrument `review files|open` stays in Organs; freeform / list / ack land here.
        if (head is "review" or "reviews" or "remark" or "remarks" or "rr")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            merged["tm_op"] = JsonSerializer.SerializeToElement("review");
            var rest = tokens.Count >= 2 ? string.Join(' ', tokens.Skip(1)) : "";
            merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "review", title = rest });
            return (merged, null);
        }

        return null;
    }

    /// <summary>Lived 2026-08-06: <c>cmd=done dig=.cdp/domain/x.md</c> swallowed dig= as task title
    /// when focus thin → <c>task not found: dig=…</c>. Strip shield kwargs into go_args; remainder = title.
    /// Pathish join mirrors <see cref="MergeWaveShipArgs"/> (Personal Cursor Folder spaces).</summary>
    static void MergeClockDoneShipArgs(
        IReadOnlyList<string> tokens,
        int skip,
        Dictionary<string, JsonElement> merged,
        string op)
    {
        var titleParts = new List<string>();
        var kwargs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        for (var i = skip; i < tokens.Count;)
        {
            var t = tokens[i];
            var eq = t.IndexOf('=');
            if (eq <= 0 || !IsClockDoneShipKey(t[..eq]))
            {
                titleParts.Add(t);
                i++;
                continue;
            }

            var key = t[..eq];
            var value = t[(eq + 1)..].Trim().Trim('"').Trim('\'');
            i++;
            if (IsPathishClockDoneKey(key))
            {
                while (i < tokens.Count && !IsClockDoneShipKeyToken(tokens[i]))
                {
                    value = (value + " " + tokens[i]).Trim();
                    i++;
                }

                value = value.Trim().Trim('"').Trim('\'');
            }

            kwargs[key] = JsonSerializer.SerializeToElement(value);
        }

        var title = string.Join(' ', titleParts).Trim();
        if (title.Length > 0)
            MergeGoArgs(merged, new { title, op });
        else
            MergeGoArgs(merged, new { op });

        if (kwargs.Count == 0)
            return;

        var ga = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (merged.TryGetValue("go_args", out var existing) && existing.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in existing.EnumerateObject())
                ga[p.Name] = p.Value.Clone();
        }

        foreach (var kv in kwargs)
            ga[kv.Key] = kv.Value;

        merged["go_args"] = JsonSerializer.SerializeToElement(ga);
    }

    static bool IsClockDoneShipKey(string key) =>
        key is "evidence" or "shot_path" or "png" or "screenshot_path"
            or "domain" or "stamp" or "domain_id" or "card" or "force"
            or "project_root" or "workspace_path" or "dig" or "dig_path"
            or "kb" or "pack" or "source" or "source_url" or "browser";

    static bool IsPathishClockDoneKey(string key) =>
        key is "evidence" or "shot_path" or "png" or "screenshot_path"
            or "project_root" or "workspace_path" or "dig" or "dig_path"
            or "kb" or "source" or "source_url";

    static bool IsClockDoneShipKeyToken(string tok)
    {
        var eq = tok.IndexOf('=');
        if (eq <= 0)
            return false;
        return IsClockDoneShipKey(tok[..eq]);
    }
}
