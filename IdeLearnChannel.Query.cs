#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeLearnChannel
{
    static object List(IReadOnlyDictionary<string, JsonElement> args)
    {
        var limit = OptInt(args, "limit") ?? 20;
        if (limit < 1)
            limit = 1;
        if (limit > 200)
            limit = 200;
        var topic = Opt(args, "topic")?.Trim();
        var tagFilter = ParseTags(Opt(args, "tags"));
        var all = LoadAll().Where(e => topic is null || string.Equals(e.Topic, topic, StringComparison.OrdinalIgnoreCase)).Where(e => tagFilter.Count == 0 || (e.Tags is not null && tagFilter.All(t => e.Tags.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))))).Reverse().Take(limit).Select(Summarize).ToArray();
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "list",
            go = GoName,
            count = all.Length,
            journal = JournalPath,
            entries = all,
            hint = all.Length == 0 ? "Empty — op=stash title= body=" : "op=recall id= or op=promote id="
        };
    }

    static object Recall(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!TryResolve(args, out var entry, out var fail))
            return fail!;
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "recall",
            go = GoName,
            entry,
            markdown = RenderMd(entry),
            next = new object[]
            {
                new
                {
                    go = "learn",
                    label = "Promote",
                    why = $"op=promote id={entry.Id}"}
            },
            hint = entry.PromotedPath is { Length: > 0 } ? $"Already promoted → {entry.PromotedPath}" : "Still journal-only — promote to survive beyond ws."
        };
    }
}