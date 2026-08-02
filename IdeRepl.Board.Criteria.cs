#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>criteria / criterion work-unit verbs.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryBoardCriteria(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
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
