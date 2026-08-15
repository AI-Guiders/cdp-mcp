#nullable enable
using System.Net.Http;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>HTTP fingerprint probe for one watch URL (ETag / LM / feed / body hash).</summary>
internal static class IdeFreshnessProbe
{
    public sealed record Result(bool Changed, object Payload, IdeFreshnessCache.Entry? Next);

    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "CdpMcp-Freshness/0.5 (+https://github.com/KarataevDmitry/cdp-mcp; KB digest)");
        return c;
    }

    public static async Task<Result> ProbeAsync(
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
                return new Result(false, EntryPayload(url, false, "not_modified", null, null, etag, lastMod, null, null, null), next);
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
                return new Result(
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
            return new Result(
                changed,
                EntryPayload(url, changed, isNew ? "first_see" : "page", title, null, etag, lastMod, Trunc(body, 200), null, null),
                nextPage);
        }
        catch (Exception ex)
        {
            return new Result(
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
        feed_items = feedItems,
        nrt = IdeFreshnessNrt.HintForUrl(url, changed, title)
    };

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
}
