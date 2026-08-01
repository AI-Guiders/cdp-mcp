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
        var primary = FirstNonEmpty(
            Opt(args, "primary"),
            Opt(args, "project_id"),
            bodyPrimary,
            latch?.Primary);
        var activeScope = FirstNonEmpty(
            Opt(args, "scope"),
            Opt(args, "active_scope"),
            bodyScope,
            latch?.Scope);
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
                new { go = "learn", label = "List", why = "op=list" },
                new { go = "learn", label = "Promote", why = $"op=promote id={entry.Id}" }
            },
            hint = "Stashed in session journal. promote when it should outlive the thread."
        };
    }

    static object List(IReadOnlyDictionary<string, JsonElement> args)
    {
        var limit = OptInt(args, "limit") ?? 20;
        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;
        var topic = Opt(args, "topic")?.Trim();
        var tagFilter = ParseTags(Opt(args, "tags"));

        var all = LoadAll()
            .Where(e => topic is null || string.Equals(e.Topic, topic, StringComparison.OrdinalIgnoreCase))
            .Where(e => tagFilter.Count == 0 || (e.Tags is not null && tagFilter.All(t =>
                e.Tags.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))))
            .Reverse()
            .Take(limit)
            .Select(Summarize)
            .ToArray();

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "list",
            go = GoName,
            count = all.Length,
            journal = JournalPath,
            entries = all,
            hint = all.Length == 0
                ? "Empty — op=stash title= body="
                : "op=recall id= or op=promote id="
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
                new { go = "learn", label = "Promote", why = $"op=promote id={entry.Id}" }
            },
            hint = entry.PromotedPath is { Length: > 0 }
                ? $"Already promoted → {entry.PromotedPath}"
                : "Still journal-only — promote to survive beyond ws."
        };
    }

    static object Promote(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!TryResolve(args, out var entry, out var fail))
            return fail!;

        var path = (Opt(args, "path") ?? Opt(args, "file_path") ?? "").Trim().Replace('\\', '/');
        if (path.Length == 0)
            path = $"{DefaultPromotePrefix}/{entry.Id}.md";

        var md = RenderMd(entry);
        string? writerKind;
        string? writeResult;
        string? localPath = null;

        if (s_knowledgeWrite is not null)
        {
            writerKind = "memory_project";
            try
            {
                writeResult = s_knowledgeWrite(path, md);
            }
            catch (Exception ex)
            {
                return Fail("promote_failed", ex.Message);
            }
        }
        else
        {
            writerKind = "local_fallback";
            localPath = Path.Combine(CdpProfile.StateRoot, "learn", SanitizeFile(entry.Id) + ".md");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                File.WriteAllText(localPath, md, Encoding.UTF8);
                writeResult = "OK";
                path = localPath;
            }
            catch (Exception ex)
            {
                return Fail("promote_failed", ex.Message);
            }
        }

        entry.PromotedPath = path;
        entry.PromotedUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        Upsert(entry);

        // Mirror under project .cdp/learn when open (repo-local durable).
        string? projectMirror = null;
        if (!string.IsNullOrWhiteSpace(session.ProjectRoot))
        {
            try
            {
                var mirrorDir = Path.Combine(session.ProjectRoot, ".cdp", "learn");
                Directory.CreateDirectory(mirrorDir);
                projectMirror = Path.Combine(mirrorDir, SanitizeFile(entry.Id) + ".md");
                File.WriteAllText(projectMirror, md, Encoding.UTF8);
            }
            catch
            {
                projectMirror = null;
            }
        }

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "promote",
            go = GoName,
            id = entry.Id,
            path,
            local_path = localPath,
            project_mirror = projectMirror,
            writer = writerKind,
            write_result = writeResult,
            pulse = PulseLine(),
            hint = "Promoted — durable beyond compaction. Journal entry marked."
        };
    }

    static object Fail(string code, string message) => new
    {
        schema = SchemaVersion,
        ok = false,
        go = GoName,
        tool = ToolName,
        error = code,
        message,
        hint = message
    };

    static bool TryResolve(
        IReadOnlyDictionary<string, JsonElement> args,
        out LearnEntry entry,
        out object? fail)
    {
        entry = null!;
        fail = null;
        var id = (Opt(args, "id") ?? "").Trim();
        var all = LoadAll();
        if (all.Count == 0)
        {
            fail = Fail("empty", "No learn cards — op=stash first");
            return false;
        }

        if (id.Length == 0 || id is "latest" or "last")
        {
            entry = all[^1];
            return true;
        }

        var hit = all.LastOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
        {
            fail = Fail("not_found", $"No learn card id={id}");
            return false;
        }

        entry = hit;
        return true;
    }

    static object Summarize(LearnEntry e) => new
    {
        e.Id,
        e.AtUtc,
        e.Title,
        e.Topic,
        e.Tags,
        e.Primary,
        e.Scope,
        promoted = e.PromotedPath is { Length: > 0 },
        e.PromotedPath,
        preview = FirstLine(e.Body ?? "", 96)
    };

    static string RenderMd(LearnEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {e.Title}");
        sb.AppendLine();
        sb.AppendLine($"- id: `{e.Id}`");
        sb.AppendLine($"- at_utc: {e.AtUtc}");
        if (e.Topic is { Length: > 0 })
            sb.AppendLine($"- topic: {e.Topic}");
        if (e.Primary is { Length: > 0 })
            sb.AppendLine($"- primary: {e.Primary}");
        if (e.Scope is { Length: > 0 })
            sb.AppendLine($"- scope: {e.Scope}");
        if (e.Tags is { Count: > 0 })
            sb.AppendLine($"- tags: {string.Join(", ", e.Tags)}");
        if (e.ProjectRoot is { Length: > 0 })
            sb.AppendLine($"- project_root: {e.ProjectRoot}");
        sb.AppendLine("- source: cdp_learn (Lean dialogue learning)");
        sb.AppendLine();
        sb.AppendLine("## Learning");
        sb.AppendLine();
        sb.AppendLine(e.Body ?? "");
        return sb.ToString();
    }
}

