#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft desk <c>go=freshness</c> / Meta <c>cdp_freshness</c> — facade.
/// Types: Catalog · Watchlist · Probe · Desk · Cache · Feed · Nrt · Schedule.
/// Digest ≠ Проверено stamp.
/// </summary>
internal static class IdeFreshnessChannel
{
    public const string SchemaVersion = "freshness_channel/v1";
    public const string DigestSchema = "freshness_digest/v1";
    public const string ToolName = "cdp_freshness";
    public const string GoName = "freshness";

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
        var op = (IdeFreshnessArgs.Opt(args, "op") ?? IdeFreshnessArgs.Opt(args, "cmd") ?? "scene")
            .Trim().ToLowerInvariant();
        return op switch
        {
            "pulse" or "a" => Task.FromResult(Pulse(session)),
            "aliases" or "alias" => Task.FromResult(Aliases()),
            "watchlist" or "list" => Task.FromResult(Watchlist(session, args)),
            "explain" => Task.FromResult(IdeFreshnessDesk.Explain(args)),
            "nrt" or "triggers" => Task.FromResult(IdeFreshnessDesk.Nrt(session, args)),
            "clear" or "cache_clear" => Task.FromResult(IdeFreshnessDesk.ClearCache(args)),
            "schedule" or "due" => Task.FromResult(IdeFreshnessSchedule.Scene()),
            "arm" => Task.FromResult(IdeFreshnessDesk.ArmSchedule(args)),
            "disarm" => Task.FromResult(IdeFreshnessSchedule.Disarm()),
            "tick" => IdeFreshnessDesk.TickAsync(session, args, ct),
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
            status = "W2-W4",
            ops = new[] { "scene", "pulse", "watchlist", "scan", "digest", "explain", "aliases", "clear", "nrt", "schedule", "arm", "disarm", "tick" },
            waves = new[]
            {
                "W1 wire+digest+watchlist+cache+aliases+explain",
                "W2 richer explain + domain NRT fire heuristics + cache clear",
                "W3 timer arm / nightly digest (op=arm|tick)",
                "W4 dogfood hot domains → Проверено stamps"
            }
        },
        schedule = IdeFreshnessSchedule.Scene(),
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
            new { op = "nrt", why = "status-* Next review triggers for alias/domain" },
            new { op = "clear", why = "drop cache entries" },
            new { op = "arm", why = "nightly/timer schedule" },
            new { op = "tick", why = "run scan when due" }
        },
        hint = "MLP freshness desk: harness walks links; Digest/Atom-shaped entries. go=freshness op=scan alias=baseline2026"
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
        aliases = IdeFreshnessCatalog.Aliases
            .Select(kv => new { id = kv.Key, url = kv.Value.Url, domain = kv.Value.Domain })
            .OrderBy(a => a.id)
            .ToArray(),
        hint = "op=scan alias=avalonia | urls=https://... | domain=software-javascript"
    };

    static object Watchlist(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var urls = IdeFreshnessWatchlist.Resolve(session, args, out var source);
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

    internal static async Task<object> ScanAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string opName,
        CancellationToken ct)
    {
        var urls = IdeFreshnessWatchlist.Resolve(session, args, out var source);
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

        var take = IdeFreshnessArgs.OptInt(args, "take") ?? 12;
        take = Math.Clamp(take, 1, 40);
        urls = urls.Take(take).ToList();

        var persist = IdeFreshnessArgs.OptBool(args, "persist") ?? true;
        var cache = IdeFreshnessCache.Load();
        var observed = DateTimeOffset.UtcNow.ToString("O");
        var entries = new List<object>();
        var changed = 0;

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            var row = await IdeFreshnessProbe.ProbeAsync(url, cache, observed, ct).ConfigureAwait(false);
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
}
