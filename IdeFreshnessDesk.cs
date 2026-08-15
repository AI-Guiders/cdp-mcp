#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Desk ops: explain / nrt / clear / arm / tick (not HTTP probe).</summary>
internal static class IdeFreshnessDesk
{
    public static object Explain(IReadOnlyDictionary<string, JsonElement> args)
    {
        var url = IdeFreshnessArgs.Opt(args, "url") ?? IdeFreshnessArgs.Opt(args, "alias");
        if (string.IsNullOrWhiteSpace(url))
        {
            return new
            {
                schema = IdeFreshnessChannel.SchemaVersion,
                ok = false,
                op = "explain",
                error = "url_or_alias_required",
                hint = "url= or alias="
            };
        }

        if (IdeFreshnessCatalog.TryMapUrl(url!, out var mapped))
            url = mapped;

        var key = IdeFreshnessCache.Key(url!);
        var cache = IdeFreshnessCache.Load();
        cache.Entries.TryGetValue(key, out var prev);
        double? ageHours = null;
        if (prev?.ObservedUtc is not null && DateTimeOffset.TryParse(prev.ObservedUtc, out var observed))
            ageHours = Math.Round((DateTimeOffset.UtcNow - observed).TotalHours, 2);

        var nrt = IdeFreshnessNrt.HintForUrl(url!, changed: false, title: prev?.FeedLatestTitle);
        return new
        {
            schema = IdeFreshnessChannel.SchemaVersion,
            ok = true,
            op = "explain",
            url,
            cached = prev,
            age_hours = ageHours,
            fingerprints = prev is null ? null : new
            {
                etag = prev.Etag,
                last_modified = prev.LastModified,
                body_hash = prev.BodyHash,
                feed_latest_id = prev.FeedLatestId,
                feed_latest_title = prev.FeedLatestTitle
            },
            nrt,
            how = new[]
            {
                "Compare ETag / Last-Modified when present",
                "If body is Atom/RSS — compare latest entry id/title",
                "Else body SHA-256 of capped GET",
                "changed=true means fingerprint differs from cache (or first see)",
                "On changed → dig status-* NRT (op=nrt); Digest ≠ Проверено"
            },
            next = new[]
            {
                new { op = "scan", why = "refresh fingerprint" },
                new { op = "nrt", why = "peel status Next review triggers" },
                new { op = "clear", why = "drop this url from cache" }
            },
            hint = "explain reads cache only — run op=scan to refresh. Digest ≠ Проверено."
        };
    }

    public static object Nrt(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var key = IdeFreshnessArgs.Opt(args, "alias") ?? IdeFreshnessArgs.Opt(args, "url")
            ?? IdeFreshnessArgs.Opt(args, "domain") ?? IdeFreshnessArgs.Opt(args, "world");
        if (string.IsNullOrWhiteSpace(key))
        {
            return new
            {
                schema = "freshness_nrt/v1",
                ok = false,
                error = "alias_or_domain_required",
                hint = "alias=avalonia | domain=software-php-laravel"
            };
        }

        return IdeFreshnessNrt.ExplainNrt(session, key!);
    }

    public static object ClearCache(IReadOnlyDictionary<string, JsonElement> args)
    {
        var url = IdeFreshnessArgs.Opt(args, "url") ?? IdeFreshnessArgs.Opt(args, "alias");
        if (string.IsNullOrWhiteSpace(url))
        {
            var n = IdeFreshnessCache.ClearAll();
            return new
            {
                schema = IdeFreshnessChannel.SchemaVersion,
                ok = true,
                op = "clear",
                cleared = n,
                scope = "all",
                path = IdeFreshnessCache.PathOnDisk,
                hint = "cache emptied"
            };
        }

        var keys = new List<string>();
        foreach (var a in IdeFreshnessArgs.SplitCsv(url))
        {
            if (IdeFreshnessCatalog.TryMapUrl(a, out var mapped)) keys.Add(mapped);
            else keys.Add(a);
        }

        var removed = IdeFreshnessCache.ClearKeys(keys);
        return new
        {
            schema = IdeFreshnessChannel.SchemaVersion,
            ok = true,
            op = "clear",
            cleared = removed,
            scope = "keys",
            keys,
            path = IdeFreshnessCache.PathOnDisk,
            hint = removed == 0 ? "no matching cache keys" : "cleared selected keys"
        };
    }

    public static object ArmSchedule(IReadOnlyDictionary<string, JsonElement> args)
    {
        var when = IdeFreshnessArgs.Opt(args, "when");
        var inRaw = IdeFreshnessArgs.Opt(args, "in");
        var take = IdeFreshnessArgs.OptInt(args, "take") ?? 12;
        var repeat = IdeFreshnessArgs.OptBool(args, "repeat") ?? true;
        var aliases = IdeFreshnessArgs.SplitCsv(
            IdeFreshnessArgs.Opt(args, "alias") ?? IdeFreshnessArgs.Opt(args, "aliases")).ToList();
        return IdeFreshnessSchedule.Arm(when, inRaw, aliases, take, repeat);
    }

    public static async Task<object> TickAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        var force = IdeFreshnessArgs.OptBool(args, "force") ?? false;
        var store = IdeFreshnessSchedule.Load();
        if (!force && !IdeFreshnessSchedule.IsDue(store))
        {
            return new
            {
                schema = IdeFreshnessChannel.DigestSchema,
                ok = true,
                op = "tick",
                skipped = true,
                reason = store.Armed ? "not_due" : "not_armed",
                schedule = IdeFreshnessSchedule.Scene(),
                hint = "force=true to scan now, or wait until due_utc"
            };
        }

        var aliasCsv = store.Aliases.Count > 0 ? string.Join(',', store.Aliases) : "avalonia,baseline2026,php";
        var scanArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["alias"] = JsonSerializer.SerializeToElement(aliasCsv),
            ["take"] = JsonSerializer.SerializeToElement(store.Take),
            ["persist"] = JsonSerializer.SerializeToElement(true)
        };
        var digest = await IdeFreshnessChannel.ScanAsync(session, scanArgs, "tick", ct).ConfigureAwait(false);
        var changed = 0;
        try
        {
            var el = JsonSerializer.SerializeToElement(digest);
            if (el.TryGetProperty("changed_count", out var cc) && cc.TryGetInt32(out var n))
                changed = n;
        }
        catch { /* ignore */ }

        IdeFreshnessSchedule.MarkTick(store, changed, DateTimeOffset.UtcNow);
        return new
        {
            schema = IdeFreshnessChannel.DigestSchema,
            ok = true,
            op = "tick",
            skipped = false,
            schedule = IdeFreshnessSchedule.Scene(),
            digest,
            hint = "Digest ≠ Проверено. Dig fire_suggested rows then stamp kb."
        };
    }
}
