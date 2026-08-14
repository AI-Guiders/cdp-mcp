#nullable enable
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=freshness</c> / Meta <c>cdp_freshness</c> — harness walks watchlist URLs;
/// agent receives Digest (not raw HTML). Digest ≠ Проверено stamp.
/// MLP W1: scene|watchlist|scan|digest|explain|aliases (+ cache).
/// </summary>
internal static partial class IdeFreshnessChannel
{
    public const string SchemaVersion = "freshness_channel/v1";
    public const string DigestSchema = "freshness_digest/v1";
    public const string ToolName = "cdp_freshness";
    public const string GoName = "freshness";

    static readonly HttpClient Http = CreateHttp();
    static readonly Regex HttpsUrl = new(
        @"https?://[^\s\)\]""'<>]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Dictionary<string, string> BuiltInAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["baseline2026"] = "https://web.dev/baseline/2026",
        ["baseline"] = "https://web.dev/baseline/2026",
        ["php-releases"] = "https://www.php.net/releases/",
        ["php"] = "https://www.php.net/releases/",
        ["laravel-releases"] = "https://laravel.com/docs/13.x/releases",
        ["laravel"] = "https://laravel.com/docs/13.x/releases",
        ["avalonia-releases"] = "https://github.com/AvaloniaUI/Avalonia/releases.atom",
        ["avalonia"] = "https://github.com/AvaloniaUI/Avalonia/releases.atom",
        ["nodejs-releases"] = "https://nodejs.org/en/blog/rss.xml",
        ["node"] = "https://nodejs.org/en/blog/rss.xml",
    };

    static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "CdpMcp-Freshness/0.5 (+https://github.com/KarataevDmitry/cdp-mcp; KB digest)");
        return c;
    }

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args).GetAwaiter().GetResult(),
            new JsonSerializerOptions { WriteIndented = true });

    public static async Task<string> HandleJsonAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args,
        CancellationToken ct) =>
        JsonSerializer.Serialize(await Handle(session, args, ct).ConfigureAwait(false),
            new JsonSerializerOptions { WriteIndented = true });

    public static Task<object> Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        CancellationToken ct = default)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "pulse" or "a" => Task.FromResult(Pulse(session)),
            "aliases" or "alias" => Task.FromResult(Aliases()),
            "watchlist" or "list" => Task.FromResult(Watchlist(session, args)),
            "explain" => Task.FromResult(Explain(args)),
            "scan" or "digest" or "check" => ScanAsync(session, args, op == "digest" ? "digest" : "scan", ct),
            _ => Task.FromResult(Scene(session))
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var cache = IdeFreshnessCache.Load();
        return $"freshness · cache×{cache.Entries.Count} · digest≠stamp";
    }

    static object Scene(SessionContext session) => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "scene",
        go = GoName,
        tool = ToolName,
        pulse = PulseLine(session),
        mlp = new
        {
            status = "W1",
            ops = new[] { "scene", "pulse", "watchlist", "scan", "digest", "explain", "aliases" },
            waves = new[]
            {
                "W1 wire+digest+watchlist+cache+aliases+explain",
                "W2 richer explain + domain NRT fire heuristics",
                "W3 timer arm / nightly digest",
                "W4 dogfood hot domains → Проверено stamps"
            }
        },
        safety = new
        {
            auto_write_knowledge = false,
            digest_is_not_provereno = true,
            note = "Agent digs delta then stamps Проверено / edits kb."
        },
        cache = new { path = IdeFreshnessCache.PathOnDisk, count = IdeFreshnessCache.Load().Entries.Count },
        next = new[]
        {
            new { op = "aliases", why = "built-in watch aliases" },
            new { op = "watchlist", why = "domain= or urls= or alias=" },
            new { op = "scan", why = "fetch + cache compare → digest entries" },
            new { op = "digest", why = "alias of scan (feed-shaped result)" }
        },
        hint = "MLP freshness: harness walks links; you get Digest/Atom-shaped entries. go=freshness op=scan alias=baseline2026"
    };

    static object Pulse(SessionContext session) => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "pulse",
        go = GoName,
        tool = ToolName,
        pulse = PulseLine(session),
        hint = "A=pulse; C=op=scene|scan"
    };

    static object Aliases() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "aliases",
        go = GoName,
        tool = ToolName,
        aliases = BuiltInAliases.Select(kv => new { id = kv.Key, url = kv.Value }).OrderBy(a => a.id).ToArray(),
        hint = "op=scan alias=avalonia | urls=https://... | domain=software-javascript"
    };

    static object Explain(IReadOnlyDictionary<string, JsonElement> args)
    {
        var url = Opt(args, "url") ?? Opt(args, "alias");
        if (string.IsNullOrWhiteSpace(url))
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                op = "explain",
                error = "url_or_alias_required",
                hint = "url= or alias="
            };
        }

        if (BuiltInAliases.TryGetValue(url!, out var mapped))
            url = mapped;

        var key = IdeFreshnessCache.Key(url!);
        var cache = IdeFreshnessCache.Load();
        cache.Entries.TryGetValue(key, out var prev);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "explain",
            url,
            cached = prev,
            how = new[]
            {
                "Compare ETag / Last-Modified when present",
                "If body is Atom/RSS — compare latest entry id/title",
                "Else body SHA-256 of capped GET",
                "changed=true means fingerprint differs from cache (or first see)"
            },
            hint = "explain reads cache only — run op=scan to refresh. Digest ≠ Проверено."
        };
    }

    static object Watchlist(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var urls = ResolveWatchlist(session, args, out var source);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "watchlist",
            go = GoName,
            tool = ToolName,
            source,
            count = urls.Count,
            urls = urls.Take(80).ToArray(),
            truncated = urls.Count > 80,
            hint = "Pass same domain=/urls=/alias= to op=scan"
        };
    }

    static async Task<object> ScanAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string opName,
        CancellationToken ct)
    {
        var urls = ResolveWatchlist(session, args, out var source);
        if (urls.Count == 0)
        {
            return new
            {
                schema = DigestSchema,
                ok = false,
                op = opName,
                error = "watchlist_empty",
                hint = "alias=baseline2026 | urls=https://a,https://b | domain=software-javascript"
            };
        }

        var take = OptInt(args, "take") ?? 12;
        take = Math.Clamp(take, 1, 40);
        urls = urls.Take(take).ToList();

        var persist = OptBool(args, "persist") ?? true;
        var cache = IdeFreshnessCache.Load();
        var observed = DateTimeOffset.UtcNow.ToString("O");
        var entries = new List<object>();
        var changed = 0;

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            var row = await ProbeAsync(url, cache, observed, ct).ConfigureAwait(false);
            if (row.Changed) changed++;
            entries.Add(row.Payload);
            if (persist && row.Next is not null)
                cache.Entries[IdeFreshnessCache.Key(url)] = row.Next;
        }

        if (persist)
            IdeFreshnessCache.Save(cache);

        return new
        {
            schema = DigestSchema,
            ok = true,
            op = opName,
            go = GoName,
            tool = ToolName,
            observed_at = observed,
            source,
            count = entries.Count,
            changed_count = changed,
            persist,
            entries,
            feed = new
            {
                title = "CDP freshness digest",
                updated = observed,
                item_count = entries.Count
            },
            hint = "Digest ≠ Проверено. Dig deltas, then stamp kb via memory_world_*. Cache: " + IdeFreshnessCache.PathOnDisk
        };
    }

    sealed record ProbeResult(bool Changed, object Payload, IdeFreshnessCache.Entry? Next);

    static async Task<ProbeResult> ProbeAsync(
        string url,
        IdeFreshnessCache.Store cache,
        string observedUtc,
        CancellationToken ct)
    {
        cache.Entries.TryGetValue(IdeFreshnessCache.Key(url), out var prev);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(prev?.Etag))
                req.Headers.TryAddWithoutValidation("If-None-Match", prev!.Etag);
            if (!string.IsNullOrWhiteSpace(prev?.LastModified))
                req.Headers.TryAddWithoutValidation("If-Modified-Since", prev!.LastModified);

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var etag = resp.Headers.ETag?.Tag;
            var lastMod = resp.Content.Headers.LastModified?.ToString("R");

            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                var next = Clone(prev, url, observedUtc, etag, lastMod, prev?.BodyHash, prev?.FeedLatestId, prev?.FeedLatestTitle);
                return new ProbeResult(false, EntryPayload(url, false, "not_modified", null, null, etag, lastMod, null, null, null), next);
            }

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (body.Length > 512_000)
                body = body[..512_000];

            var ctHeader = resp.Content.Headers.ContentType?.ToString();
            if (IdeFreshnessFeed.LooksLikeFeed(ctHeader, body))
            {
                var items = IdeFreshnessFeed.Parse(body, take: 5);
                var latest = items.FirstOrDefault();
                var latestId = latest?.Id;
                var latestTitle = latest?.Title;
                var isChanged = prev is null
                    || !string.Equals(prev.FeedLatestId, latestId, StringComparison.Ordinal)
                    || (!string.IsNullOrEmpty(latestTitle) && !string.Equals(prev.FeedLatestTitle, latestTitle, StringComparison.Ordinal));
                var next = Clone(prev, url, observedUtc, etag, lastMod, null, latestId, latestTitle);
                return new ProbeResult(
                    isChanged,
                    EntryPayload(
                        url, isChanged, "feed",
                        latestTitle, latest?.Published, etag, lastMod, latest?.Summary, latestId,
                        items.Select(i => new { id = i.Id, title = i.Title, published = i.Published, link = i.Link }).ToArray()),
                    next);
            }

            var hash = IdeFreshnessFeed.Sha256Hex(body);
            var changedHash = prev?.BodyHash is null || !string.Equals(prev.BodyHash, hash, StringComparison.OrdinalIgnoreCase);
            var changedMeta = (!string.IsNullOrEmpty(etag) && prev?.Etag is not null && !string.Equals(prev.Etag, etag, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(lastMod) && prev?.LastModified is not null && !string.Equals(prev.LastModified, lastMod, StringComparison.Ordinal));
            var isNew = prev is null;
            var changed = isNew || changedHash || changedMeta;
            var title = GuessTitle(body);
            var nextPage = Clone(prev, url, observedUtc, etag ?? prev?.Etag, lastMod ?? prev?.LastModified, hash, null, null);
            return new ProbeResult(
                changed,
                EntryPayload(url, changed, isNew ? "first_see" : "page", title, null, etag, lastMod, Trunc(body, 200), null, null),
                nextPage);
        }
        catch (Exception ex)
        {
            return new ProbeResult(
                false,
                EntryPayload(url, false, "error", null, null, null, null, Trunc(ex.Message, 200), null, null),
                prev);
        }
    }

    static IdeFreshnessCache.Entry Clone(
        IdeFreshnessCache.Entry? prev,
        string url,
        string observedUtc,
        string? etag,
        string? lastMod,
        string? bodyHash,
        string? feedId,
        string? feedTitle) => new()
    {
        Url = url,
        Etag = etag ?? prev?.Etag,
        LastModified = lastMod ?? prev?.LastModified,
        BodyHash = bodyHash ?? prev?.BodyHash,
        FeedLatestId = feedId ?? prev?.FeedLatestId,
        FeedLatestTitle = feedTitle ?? prev?.FeedLatestTitle,
        ObservedUtc = observedUtc,
        Alias = prev?.Alias
    };

    static object EntryPayload(
        string url,
        bool changed,
        string kind,
        string? title,
        string? published,
        string? etag,
        string? lastModified,
        string? snippet,
        string? feedLatestId,
        object? feedItems) => new
    {
        url,
        changed,
        kind,
        title,
        published,
        etag,
        last_modified = lastModified,
        snippet,
        feed_latest_id = feedLatestId,
        feed_items = feedItems
    };

    internal static List<string> ResolveWatchlist(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        out string source)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in SplitCsv(Opt(args, "alias") ?? Opt(args, "aliases")))
        {
            if (BuiltInAliases.TryGetValue(a, out var u))
                set.Add(u);
            else if (a.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                set.Add(a);
        }

        foreach (var u in SplitCsv(Opt(args, "urls") ?? Opt(args, "url")))
        {
            if (BuiltInAliases.TryGetValue(u, out var mapped))
                set.Add(mapped);
            else if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                set.Add(TrimUrl(u));
        }

        var domain = Opt(args, "domain") ?? Opt(args, "world");
        if (!string.IsNullOrWhiteSpace(domain))
        {
            foreach (var u in ExtractUrlsFromDomain(session, domain!))
                set.Add(u);
        }

        var path = Opt(args, "path") ?? Opt(args, "file");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            foreach (Match m in HttpsUrl.Matches(File.ReadAllText(path)))
                set.Add(TrimUrl(m.Value));
        }

        if (set.Count == 0)
        {
            source = "empty";
            return [];
        }

        source = domain is not null ? $"domain:{domain}"
            : path is not null ? $"file:{path}"
            : Opt(args, "alias") is not null ? "alias"
            : "urls";
        return set.OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static IEnumerable<string> ExtractUrlsFromDomain(SessionContext session, string domain)
    {
        domain = domain.Trim().Replace('\\', '/').Trim('/');
        if (domain.StartsWith("worlds/", StringComparison.OrdinalIgnoreCase))
            domain = domain["worlds/".Length..];

        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.ProjectRoot))
        {
            roots.Add(Path.Combine(session.ProjectRoot, "knowledge", "worlds", domain));
            roots.Add(Path.Combine(session.ProjectRoot, "worlds", domain));
        }

        var canon = Environment.GetEnvironmentVariable("AGENT_NOTES_CANON_PATH");
        if (!string.IsNullOrWhiteSpace(canon))
            roots.Add(Path.Combine(canon, "knowledge", "worlds", domain));

        roots.Add(Path.Combine(@"D:\Experiments\agent-notes", "knowledge", "worlds", domain));

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly).Take(80))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }
                foreach (Match m in HttpsUrl.Matches(text))
                    yield return TrimUrl(m.Value);
            }
            yield break;
        }
    }

    static IEnumerable<string> SplitCsv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var p in raw.Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return p;
    }

    static string TrimUrl(string u)
    {
        u = u.Trim().TrimEnd('.', ',', ';', ')', ']');
        return u;
    }

    static string? GuessTitle(string body)
    {
        var m = Regex.Match(body, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        return Trunc(Regex.Replace(m.Groups[1].Value, @"\s+", " "), 120);
    }

    static string? Trunc(string? s, int n)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= n ? s : s[..n] + "…";
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
    }

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n)) return n;
        return null;
    }

    static bool? OptBool(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind is JsonValueKind.True) return true;
        if (el.ValueKind is JsonValueKind.False) return false;
        if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b)) return b;
        return null;
    }
}
