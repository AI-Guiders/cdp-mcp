#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeQrhChannel
{
    public static object Handle(
        IdeChkChannel.ProbeCtx ctx,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        IdeChkChannel.Snap? ecl = null)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "index").Trim().ToLowerInvariant();
        var suggest = SuggestFor(ctx, ecl);

        if (op is "add" or "upsert")
            return DoAdd(merged);
        if (op is "remove" or "rm" or "delete")
            return DoRemove(merged);
        if (op is "enable" or "on")
            return DoEnable(merged, enable: true);
        if (op is "disable" or "off")
            return DoEnable(merged, enable: false);
        if (op is "overlay")
            return OverlayScene();

        if (op is "index" or "list" or "catalog" or "scene")
            return Board(null, suggest, action: null, mode: "index");

        if (op is "shelf" or "section")
        {
            var shelf = (Opt(merged, "shelf") ?? Opt(merged, "section") ?? Opt(merged, "id") ?? "").Trim();
            var pages = AllPages()
                .Where(p => p.Shelf.Equals(shelf, StringComparison.OrdinalIgnoreCase))
                .Select(IndexCard)
                .ToArray();
            return new
            {
                ok = true,
                go = "qrh",
                schema = SchemaVersion,
                mode = "shelf",
                shelf,
                pulse = $"qrh · {shelf} ×{pages.Length}",
                title = "eQRH",
                pages,
                suggest = SuggestCard(suggest),
                hint = "op=open id=… | op=search q=…"
            };
        }

        if (op is "search" or "find" or "q")
        {
            var q = (Opt(merged, "q") ?? Opt(merged, "query") ?? Opt(merged, "id") ?? "").Trim();
            if (q.Length == 0)
                return Err("q_required", "qrh search q=pdb | qrh open dap-pdb-lock");
            var hits = Search(q);
            object? opened = hits.Count == 1 ? PageCard(hits[0]) : null;
            return new
            {
                ok = true,
                go = "qrh",
                schema = SchemaVersion,
                mode = hits.Count == 1 ? "open" : "search",
                pulse = hits.Count == 1 ? $"qrh · {hits[0].Id}" : $"qrh · search ×{hits.Count}",
                title = "eQRH",
                query = q,
                hits = hits.Select(IndexCard).ToArray(),
                page = opened,
                suggest = SuggestCard(suggest),
                hint = hits.Count == 0 ? "No page — try qrh index" : "op=open id=…"
            };
        }

        if (op is "open" or "page" or "show" or "get")
        {
            var id = Opt(merged, "id") ?? Opt(merged, "page") ?? Opt(merged, "name") ?? suggest.HotId;
            if (id is not { Length: > 0 })
                return Err("id_required", "qrh open dap-pdb-lock | qrh open (uses SA suggest)");
            var page = Find(id);
            if (page is null)
            {
                var hits = Search(id);
                if (hits.Count == 1) page = hits[0];
            }

            if (page is null)
                return Err("not_found", $"No QRH page '{id}' — qrh index");

            return Board(page, suggest, action: new { ok = true, op = "open", id = page.Id }, mode: "open");
        }

        if (op is "related" or "suggest")
        {
            var from = Opt(merged, "id") ?? Opt(merged, "from") ?? suggest.HotId;
            var page = from is { Length: > 0 } ? Find(from) : null;
            var related = page is null
                ? suggest.RelatedIds.Select(Find).Where(p => p is not null).Cast<Page>().ToArray()
                : page.Related.Select(Find).Where(p => p is not null).Cast<Page>().ToArray();
            return new
            {
                ok = true,
                go = "qrh",
                schema = SchemaVersion,
                mode = "related",
                pulse = $"qrh · related ×{related.Length}",
                from,
                pages = related.Select(IndexCard).ToArray(),
                suggest = SuggestCard(suggest),
                hint = "op=open id=…"
            };
        }

        return Err("unknown_op", "op=index|open|search|shelf|related|add|remove|overlay");
    }

    static object Board(Page? page, Suggest suggest, object? action, string mode)
    {
        var related = page is null
            ? Array.Empty<object>()
            : page.Related.Select(Find).Where(p => p is not null).Cast<Page>().Select(IndexCard).ToArray();

        return new
        {
            ok = true,
            go = "qrh",
            schema = SchemaVersion,
            mode,
            pulse = page is null ? suggest.Pulse : $"qrh · {page.Id}",
            title = "eQRH",
            note = "Electronic QRH — systems / abnormal / emergency. Pack cards via anchors; desk projector, not memory_* thrash.",
            page = page is null ? null : PageCard(page),
            related,
            index = AllPages().Select(IndexCard).ToArray(),
            suggest = SuggestCard(suggest),
            shelves = new[] { "systems", "abnormal", "emergency" },
            action,
            hint = "CCL: qrh | qrh open dap-pdb-lock | qrh add id=… | qrh search pdb"
        };
    }

    static object IndexCard(Page p) => new
    {
        id = p.Id,
        shelf = p.Shelf,
        title = p.Title,
        condition = Trunc(p.Condition, 96),
        signals = p.Signals
    };

    static object PageCard(Page p) => new
    {
        id = p.Id,
        shelf = p.Shelf,
        title = p.Title,
        condition = p.Condition,
        signals = p.Signals,
        memory_items = p.MemoryItems,
        steps = p.Steps.Select(s => new { text = s.Text, go = s.Go, action = s.Action }).ToArray(),
        related = p.Related,
        pack_anchors = p.PackAnchors,
        llm_cue = p.LlmCue
    };

    static object SuggestCard(Suggest s) => new
    {
        hot = s.HotId,
        related = s.RelatedIds,
        pulse = s.Pulse
    };

    public static Page? Find(string id) =>
        AllPages().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<Page> Search(string q)
    {
        var needle = q.Trim();
        if (needle.Length == 0) return [];
        bool Match(Page p) =>
            p.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || p.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || p.Condition.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || p.Shelf.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || p.Signals.Any(s => s.Contains(needle, StringComparison.OrdinalIgnoreCase))
            || p.MemoryItems.Any(m => m.Contains(needle, StringComparison.OrdinalIgnoreCase))
            || p.PackAnchors.Any(a => a.Contains(needle, StringComparison.OrdinalIgnoreCase));

        return AllPages().Where(Match).ToArray();
    }

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    static object Err(string error, string hint) => new
    {
        ok = false,
        go = "qrh",
        schema = SchemaVersion,
        error,
        hint
    };

    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null) return d;
        foreach (var kv in args) d[kv.Key] = kv.Value;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
                d[p.Name] = p.Value.Clone();
        }

        return d;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}

