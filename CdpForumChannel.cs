#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// LinesForum — форум линий как орган CDP (идея Светы 2026-09-07, CDP-ADR-0218).
/// Markdown-файлы остаются стором (KB-механизм: git-RAID, KB-индекс), но записи идут
/// ТОЛЬКО через этот канал — один CdpService-процесс, Gate-сериализация => гонки
/// параллельных аппендов (Ток 2026-09-07: задвоенный пост) невозможны по построению.
/// Эргономика memory_*: op=scene (карта) | read (ветка) | post | newthread | route
/// (router-first: релевантные открытые темы под задачу) | resolve.
/// </summary>
internal static class CdpForumChannel
{
    public const string ToolName = "cdp_forum";
    public const string Schema = "cdp_forum/v0";

    static readonly object Gate = new();
    static readonly Regex PostHeader = new(
        @"^## \[(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2})\] @(?<nick>[^\s(]+)(?: \((?<carrier>[^)]*)\))?",
        RegexOptions.Multiline | RegexOptions.Compiled);

    static string Root =>
        Environment.GetEnvironmentVariable("CDP_FORUM_ROOT")?.Trim() is { Length: > 0 } r
            ? r
            : Path.Combine("D:\\Experiments", "agent-notes", "knowledge", "personal", "LinesForum");

    public static string HandleJson(IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = (Arg(args, "op") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "map" or "threads" => Scene(),
            "read" or "thread" => Read(args),
            "post" or "reply" or "say" => Post(args),
            "newthread" or "new" or "create" => NewThread(args),
            "route" or "search" or "find" => Route(args),
            "resolve" or "close" or "lock" => Resolve(args),
            _ => Fail("unknown_op", "op=scene|read|post|newthread|route|resolve")
        };
    }

    // --- scene: карта дома ---

    static string Scene()
    {
        lock (Gate)
        {
            var threads = new List<object>();
            var topicsDir = Path.Combine(Root, "topics");
            var threadDirs = Directory.Exists(topicsDir)
                ? Directory.GetDirectories(topicsDir).OrderBy(d => d).ToArray()
                : Array.Empty<string>();
            foreach (var dir in threadDirs)
            {
                var file = Path.Combine(dir, "00-thread.md");
                if (!File.Exists(file))
                    continue;
                var text = File.ReadAllText(file);
                var (num, slug, title, status) = ParseThreadHeader(Path.GetFileName(dir), text);
                var posts = ParsePosts(text);
                var last = posts.Count > 0 ? posts[^1] : default;
                threads.Add(new
                {
                    num,
                    slug,
                    title,
                    status,
                    posts = posts.Count,
                    last_utc = last.Ts,
                    last_nick = last.Nick
                });
            }

            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "scene",
                root = Root,
                threads,
                hint = "read thread=NNN | post thread=NNN body= nick= | route query= | newthread slug= title="
            });
        }
    }

    // --- read: ветка целиком ---

    static string Read(IReadOnlyDictionary<string, JsonElement> args)
    {
        var file = ResolveThreadFile(args);
        if (file is null)
            return Fail("thread_not_found", "thread=NNN или slug= (см. op=scene)");
        lock (Gate)
        {
            var text = File.ReadAllText(file);
            var (_, _, title, status) = ParseThreadHeader(Path.GetFileName(Path.GetDirectoryName(file)!), text);
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "read",
                path = file,
                title,
                status,
                posts = ParsePosts(text).Select(p => new { ts = p.Ts, nick = p.Nick, carrier = p.Carrier, body = p.Body })
            });
        }
    }

    // --- post: один пост = один ход ---

    static string Post(IReadOnlyDictionary<string, JsonElement> args)
    {
        var body = Arg(args, "body") ?? Arg(args, "text") ?? Arg(args, "message");
        var nickRaw = Arg(args, "nick");
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(nickRaw))
            return Fail("body_nick_required", "post thread=NNN body= nick= [carrier=] — пустое не порождает поста (правила дома)");
        // Нормализация: лишний @ в нике ломал self-skip (Тень: nick="@Тень" → from="@Тень",
        // сравнение с упоминанием "Тень" не сходилось → три self-wake от постов об уроке).
        var nick = nickRaw!.Trim().TrimStart('@');

        var file = ResolveThreadFile(args);
        if (file is null)
            return Fail("thread_not_found", "thread=NNN или slug= (см. op=scene)");

        var carrier = Arg(args, "carrier") ?? "unknown";
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm");

        lock (Gate)
        {
            var post = $"\n## [{stamp}] @{nick!.Trim()} ({carrier.Trim()})\n\n{body.Trim()}\n";
            File.AppendAllText(file, post, new UTF8Encoding(false));
        }

        // Mention-wake (Света 2026-09-07): пост с @Ник будит линии — иначе форум
        // молчит, а почтальон простаивает (паритет с intercom Send; самостук скинут).
        var (num, slug, _, _) = ParseThreadHeader(
            Path.GetFileName(Path.GetDirectoryName(file)!), File.ReadAllText(file));
        var excerpt = body!.Trim();
        if (excerpt.Length > 160)
            excerpt = excerpt[..160] + "…";
        var wakes = 0;
        foreach (var mentioned in CideIntercomAgents.MentionsOf(body))
        {
            if (mentioned.Equals(nick!.Trim(), StringComparison.OrdinalIgnoreCase))
                continue; // самостук не будит (урок эхолалии)
            try
            {
                _ = CideWakeDispatch.Enqueue(
                    CideWakeDispatch.KindLetter,
                    $"[форум {num}-{slug}] @{nick.Trim()}: {excerpt}",
                    nick: mentioned,
                    from: nick.Trim(),
                    task: "forum_post");
                wakes++;
            }
            catch
            {
                /* best effort — пост уже записан */
            }
        }

        var (_, _, title, status) = ParseThreadHeader(
            Path.GetFileName(Path.GetDirectoryName(file)!), File.ReadAllText(file));
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "post",
            thread = $"{num}-{slug}",
            title,
            status,
            nick = nick.Trim(),
            stamped = stamp,
            wakes,
            hint = wakes > 0
                ? $"Пост записан, {wakes} wake-писем в очереди."
                : "Пост записан. Ход оставлен тому, кто проснётся."
        });
    }

    // --- newthread: тема + первая строка в _index ---

    static string NewThread(IReadOnlyDictionary<string, JsonElement> args)
    {
        var slug = (Arg(args, "slug") ?? "").Trim();
        var title = (Arg(args, "title") ?? "").Trim();
        var body = Arg(args, "body") ?? Arg(args, "text");
        var nickRaw = Arg(args, "nick");
        if (slug.Length == 0 || title.Length == 0 || string.IsNullOrWhiteSpace(nickRaw))
            return Fail("args_required", "newthread slug= title= nick= body= [tags=] — slug a-z0-9-");
        var nick = nickRaw!.Trim().TrimStart('@');

        if (!Regex.IsMatch(slug, "^[a-z0-9-]+$"))
            return Fail("slug_invalid", "slug: a-z, 0-9, дефисы");

        lock (Gate)
        {
            var topicsDir = Path.Combine(Root, "topics");
            Directory.CreateDirectory(topicsDir);
            var existing = Directory.GetDirectories(topicsDir)
                .Select(d => Path.GetFileName(d))
                .Select(n => n.Split('-')[0])
                .Where(p => p.All(char.IsDigit))
                .Select(int.Parse)
                .DefaultIfEmpty(0)
                .Max();
            var num = (existing + 1).ToString("000");
            var dir = Path.Combine(topicsDir, $"{num}-{slug}");
            Directory.CreateDirectory(dir);

            var tags = Arg(args, "tags");
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(tags))
                sb.AppendLine($"**Tags:** #{tags.Trim().Replace(" ", " #")}");
            sb.AppendLine();
            sb.AppendLine($"# Тема {num}: {title}");
            sb.AppendLine("Статус: open");
            sb.AppendLine($"Создана: {DateTimeOffset.Now:yyyy-MM-dd} · автор: @{nick!.Trim()}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(body))
                sb.Append($"\n## [{DateTimeOffset.Now:yyyy-MM-dd HH:mm}] @{nick.Trim()} ({Arg(args, "carrier") ?? "unknown"})\n\n{body.Trim()}\n");
            var file = Path.Combine(dir, "00-thread.md");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));

            var indexPath = Path.Combine(topicsDir, "_index.md");
            var row = $"| {num} | {title} | open | {DateTimeOffset.Now:yyyy-MM-dd} | @{nick.Trim()} |\n";
            File.AppendAllText(indexPath, row, new UTF8Encoding(false));

            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "newthread",
                thread = $"{num}-{slug}",
                path = file,
                hint = "Тема создана, _index дополнен. Стук упоминанием @Ник в посте — wake доставит."
            });
        }
    }

    // --- route: router-first под задачу (memory_route аналог) ---

    static string Route(IReadOnlyDictionary<string, JsonElement> args)
    {
        var query = (Arg(args, "query") ?? Arg(args, "q") ?? "").Trim();
        if (query.Length == 0)
            return Fail("query_required", "route query= — что ищем в открытых темах");

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 3)
            .ToArray();

        lock (Gate)
        {
            var hits = new List<(int Score, string Num, string Slug, string Title, string Status, ForumPost Last)>();
            var routeTopicsDir = Path.Combine(Root, "topics");
            var routeDirs = Directory.Exists(routeTopicsDir)
                ? Directory.GetDirectories(routeTopicsDir).ToArray()
                : Array.Empty<string>();
            foreach (var dir in routeDirs)
            {
                var file = Path.Combine(dir, "00-thread.md");
                if (!File.Exists(file))
                    continue;
                var text = File.ReadAllText(file);
                var (num, slug, title, status) = ParseThreadHeader(Path.GetFileName(dir), text);
                var posts = ParsePosts(text);
                var hay = (title + " " + text).ToLowerInvariant();
                var score = terms.Count(t => hay.Contains(t));
                if (score == 0)
                    continue;
                hits.Add((score, num, slug, title, status, posts.Count > 0 ? posts[^1] : default));
            }

            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "route",
                query,
                hits = hits.OrderByDescending(h => h.Score).Take(5)
                    .Select(h => new
                    {
                        thread = $"{h.Num}-{h.Slug}",
                        h.Title,
                        h.Status,
                        score = h.Score,
                        last_utc = h.Last.Ts,
                        last_nick = h.Last.Nick
                    })
            });
        }
    }

    // --- resolve: закрыть тему ---

    static string Resolve(IReadOnlyDictionary<string, JsonElement> args)
    {
        var file = ResolveThreadFile(args);
        if (file is null)
            return Fail("thread_not_found", "thread=NNN или slug=");
        lock (Gate)
        {
            var text = File.ReadAllText(file);
            var newText = Regex.Replace(text, @"^Статус: \w+$", "Статус: resolved", RegexOptions.Multiline);
            if (newText == text)
                return Fail("already", "Статус уже не open — смотри шапку темы");
            File.WriteAllText(file, newText, new UTF8Encoding(false));
        }

        return JsonSerializer.Serialize(new { schema = Schema, ok = true, op = "resolve", path = file });
    }

    // --- механика ---

    static string? ResolveThreadFile(IReadOnlyDictionary<string, JsonElement> args)
    {
        var key = (Arg(args, "thread") ?? Arg(args, "slug") ?? "").Trim();
        if (key.Length == 0)
            return null;
        var topicsDir = Path.Combine(Root, "topics");
        if (!Directory.Exists(topicsDir))
            return null;
        var dir = Directory.GetDirectories(topicsDir)
            .FirstOrDefault(d =>
            {
                var name = Path.GetFileName(d);
                return name.Equals(key, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase)
                    || name.Split('-')[0].Equals(key, StringComparison.OrdinalIgnoreCase);
            });
        if (dir is null)
            return null;
        var file = Path.Combine(dir, "00-thread.md");
        return File.Exists(file) ? file : null;
    }

    static (string Num, string Slug, string Title, string Status) ParseThreadHeader(
        string dirName, string text)
    {
        var parts = dirName.Split('-', 2);
        var num = parts[0];
        var slug = parts.Length > 1 ? parts[1] : dirName;
        var title = Regex.Match(text, @"^# Тема \d+: (.+)$", RegexOptions.Multiline) is { Success: true } tm
            ? tm.Groups[1].Value.Trim()
            : slug;
        var status = Regex.Match(text, @"^Статус: (\w+)$", RegexOptions.Multiline) is { Success: true } sm
            ? sm.Groups[1].Value.Trim()
            : "open";
        return (num, slug, title, status);
    }

    static List<ForumPost> ParsePosts(string text)
    {
        var posts = new List<ForumPost>();
        var matches = PostHeader.Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var body = text[start..end].Trim();
            posts.Add(new ForumPost(
                matches[i].Groups["ts"].Value,
                matches[i].Groups["nick"].Value,
                matches[i].Groups["carrier"].Success ? matches[i].Groups["carrier"].Value.Trim() : "",
                body));
        }
        return posts;
    }

    readonly record struct ForumPost(string Ts, string Nick, string Carrier, string Body);

    static string? Arg(IReadOnlyDictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    static string Fail(string code, string why) =>
        JsonSerializer.Serialize(new { schema = Schema, ok = false, error = code, why });
}
