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
    static object Stash(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var title = (Opt(args, "title") ?? Opt(args, "name") ?? "").Trim();
        var body = (Opt(args, "body") ?? Opt(args, "text") ?? Opt(args, "learning") ?? "").Trim();
        if (body.Length == 0)
            return Fail("need_body", "op=stash body= (or text=) — concentrated learning card");
        if (title.Length == 0)
            title = FirstLine(body, 72);
        var topic = Opt(args, "topic")?.Trim();
        var tags = ParseTags(Opt(args, "tags"));
        var latch = IdeScopeChannel.CurrentOrNull();
        IdeScopeChannel.TryParseMarkers(body, out var bodyPrimary, out var bodyScope);
        var primary = FirstNonEmpty(Opt(args, "primary"), Opt(args, "project_id"), bodyPrimary, latch?.Primary);
        var activeScope = FirstNonEmpty(Opt(args, "scope"), Opt(args, "active_scope"), bodyScope, latch?.Scope);
        var now = DateTimeOffset.UtcNow;
        var id = (Opt(args, "id") ?? "").Trim();
        if (id.Length == 0)
            id = MakeId(now, title);
        var entry = new LearnEntry
        {
            Id = id,
            AtUtc = now.ToString("o", CultureInfo.InvariantCulture),
            Title = title,
            Body = body,
            Topic = string.IsNullOrWhiteSpace(topic) ? null : topic,
            Tags = tags.Count > 0 ? tags : null,
            Primary = primary,
            Scope = activeScope,
            ProjectRoot = session.ProjectRoot,
            Phase = session.Phase.ToString(),
            Object = session.Object.ToString()
        };
        Append(entry);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "stash",
            go = GoName,
            id = entry.Id,
            title = entry.Title,
            topic = entry.Topic,
            tags = entry.Tags,
            primary = entry.Primary,
            scope = entry.Scope,
            journal = JournalPath,
            count = CountEntries(),
            pulse = PulseLine(),
            next = new object[]
            {
                new
                {
                    go = "learn",
                    label = "List",
                    why = "op=list"
                },
                new
                {
                    go = "learn",
                    label = "Promote",
                    why = $"op=promote id={entry.Id}"}
            },
            hint = "Stashed in session journal. promote when it should outlive the thread."
        };
    }
}