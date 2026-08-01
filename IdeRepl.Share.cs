#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>CCL verbs peeled from Apply (soft-warn). null = not handled.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryShare(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
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

        return null;
    }
}
