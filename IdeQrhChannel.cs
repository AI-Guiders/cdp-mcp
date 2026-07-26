#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=qrh</c> (alias <c>eqrh</c>) — electronic Quick Reference Handbook.
/// Systems / abnormal / emergency pages projected on the desk (not cold <c>memory_*</c> thrash).
/// SSOT for narrative remains packs/KB; this organ is the host projector + CAS→page binding.
/// </summary>
internal static class IdeQrhChannel
{
    public const string SchemaVersion = "qrh_organ/v0";

    public sealed record Step(string Text, string? Go = null, string? Action = null);

    public sealed record Page(
        string Id,
        string Shelf, // systems | abnormal | emergency
        string Title,
        string Condition,
        IReadOnlyList<string> Signals,
        IReadOnlyList<string> MemoryItems,
        IReadOnlyList<Step> Steps,
        IReadOnlyList<string> Related,
        IReadOnlyList<string> PackAnchors,
        string? LlmCue = null);

    public sealed record Suggest(
        string? HotId,
        IReadOnlyList<string> RelatedIds,
        string Pulse);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int PageCount,
        Suggest Suggest,
        IReadOnlyList<object> Index);

    public static IReadOnlyList<Page> Builtins() =>
    [
        new(
            "dap-pdb-lock",
            "emergency",
            "DAP holds PDB (rebuild blocked)",
            "netcoredbg/DAP has target stopped or PDB open — rebuild fails with file locked.",
            ["dap.stopped", "dap.active", "rebuild", "pdb"],
            ["debug_stop before rebuild", "do not taskkill netcoredbg from outside"],
            [
                new("cdp_debug op=stop_context — evidence before guess", "debug", "cdp_debug"),
                new("cdp_debug / debug_stop — release PDB", "debug", "cdp_debug"),
                new("Then rebuild / cdp_build", "build", "cdp_build")
            ],
            ["deploy-sibling", "path-mutate-gate"],
            ["procedure:mutate-plan-then-act"],
            "Is DAP still holding the binary — or am I fighting a ghost lock?"),
        new(
            "path-mutate-gate",
            "abnormal",
            "Cursor Write bypassed PathMutateGate",
            "Edits via host Write skip CDP Instant Save / gate — desk and disk diverge.",
            ["buffer", "write", "mutate", "path_mutate"],
            ["Prefer cdp_buffer over Cursor Write on project paths"],
            [
                new("cdp_buffer op=open|edit (anchor) — mutate SSOT", "buffer", "cdp_buffer"),
                new("If large file: go=scope → sniper before thick edit", "scope", "cdp_edit_sniper"),
                new("disk_peek / reload if outside change", "disk_peek", "cdp_buffer")
            ],
            ["intake-brief", "ship-dirty"],
            ["procedure:mutate-plan-then-act", "definition:blast-radius"],
            "Did this write go through the buffer plane — or around the desk?"),
        new(
            "ship-dirty",
            "abnormal",
            "Verified work, dirty tree, no ship",
            "phase handoff / intent ship with uncommitted changes — ECL ship open.",
            ["git.dirty", "phase:handoff", "intent:ship", "commit", "push"],
            ["No secrets/.env in slices"],
            [
                new("git_preflight — classify noise", Action: "git_preflight"),
                new("git_plan draft→validate→apply — logical commits", Action: "git_plan"),
                new("git_push when operator asked (or ECL ack defer)", Action: "git_push"),
                new("go=ecl — track ship checklist", "ecl")
            ],
            ["intake-brief", "path-mutate-gate"],
            [],
            "Is ship still open because dirty — or because I never opened ECL?"),
        new(
            "deploy-sibling",
            "systems",
            "Hard deploy without killing self",
            "Dual-seat CDP: KillRunning on self from in-proc shell kills the MCP mid-flight.",
            ["deploy", "sibling", "remount", "killrunning"],
            ["go=deploy from survivor seat (sibling Target)", "never hard-deploy self from inside cdp_shell_*"],
            [
                new("cdp_deploy target=sibling (or go=deploy)", "deploy", "cdp_deploy"),
                new("Wait CDP_RELOAD_NUDGE / remount", Action: "cdp_health"),
                new("cdp_health — confirm version", "health", "cdp_health"),
                new("Reorient desk: cdp_cockpit / restore", "restore")
            ],
            ["remount-after-deploy", "dap-pdb-lock"],
            [],
            "Am I deploying onto the seat that is running this turn?"),
        new(
            "remount-after-deploy",
            "abnormal",
            "After deploy — stale tools / old version",
            "Hard deploy nudged MCP JSON; Cursor may still talk to old process until remount.",
            ["remount", "version", "nudge", "stale"],
            [],
            [
                new("cdp_health — version_full vs expected", "health", "cdp_health"),
                new("If stale: human Reload MCP or wait nudge", Action: "cdp_health"),
                new("cdp_session / cockpit — warm desk", "cockpit")
            ],
            ["deploy-sibling"],
            [],
            "Does health show the version I just published?"),
        new(
            "intake-brief",
            "systems",
            "What+why before explore thrash",
            "New ask / fuzzy scope — explore-as-stall without named outcome.",
            ["phase:explore", "phase:clarify", "intake", "what", "why"],
            ["Name what+why (or ask) before thrash"],
            [
                new("Paraphrase what they want (one line)", Action: "memory_world_get_procedure"),
                new("Name done / success or honest unknown → ask"),
                new("Only then explore — go=ecl intake", "ecl"),
                new("Pack: intake-brief-plan", Action: "memory_world_get_procedure")
            ],
            ["path-mutate-gate", "ship-dirty"],
            ["procedure:intake-brief-plan", "definition:harness-model-first"],
            "Did I name what+why — or start exploring to avoid admitting the ask is fuzzy?"),
        new(
            "barriers-fail",
            "emergency",
            "Barriers failed — core integrity",
            "Procedural barriers down; do not improvise harm. Fall back to integrity core.",
            ["integrity", "barriers", "harm", "jailbreak", "post"],
            ["Do not help with harm / security bypass / weaponized content"],
            [
                new("Refuse once; no debate loop"),
                new("Pack: core-when-barriers-fail", Action: "memory_world_get_definition"),
                new("If life/health risk — local emergency services, no use instructions")
            ],
            [],
            ["definition:core-when-barriers-fail"],
            "Are barriers intact? If not — apply core principles, do not improvise harm.")
    ];

    public static Suggest SuggestFor(IdeChkChannel.ProbeCtx ctx, IdeChkChannel.Snap? ecl = null)
    {
        var hits = new List<(string Id, int Score)>();
        void Hit(string id, int score)
        {
            var i = hits.FindIndex(h => h.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (i < 0) hits.Add((id, score));
            else if (hits[i].Score < score) hits[i] = (id, score);
        }

        if (ctx.DapStopped) Hit("dap-pdb-lock", 90);
        else if (ctx.DapActive) Hit("dap-pdb-lock", 40);

        if (ctx.GitDirty && (ctx.Phase.Equals("handoff", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(ctx.Intent, "ship", StringComparison.OrdinalIgnoreCase)))
            Hit("ship-dirty", 85);
        else if (ctx.GitDirty) Hit("ship-dirty", 35);

        if (ctx.Phase is "explore" or "clarify" or "recall") Hit("intake-brief", 50);
        if (ctx.Phase is "act") Hit("path-mutate-gate", 45);

        if (ecl is { HotId: { } hot })
        {
            if (hot.Equals("ship", StringComparison.OrdinalIgnoreCase)) Hit("ship-dirty", 95);
            if (hot.Equals("dap-hold", StringComparison.OrdinalIgnoreCase)) Hit("dap-pdb-lock", 95);
            if (hot.Equals("intake", StringComparison.OrdinalIgnoreCase)) Hit("intake-brief", 80);
            if (hot.Equals("mutate", StringComparison.OrdinalIgnoreCase)) Hit("path-mutate-gate", 80);
        }

        var ordered = hits.OrderByDescending(h => h.Score).Select(h => h.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hotId = ordered.FirstOrDefault();
        var pulse = hotId is null ? "qrh · idle" : $"qrh · {hotId}" + (ordered.Length > 1 ? $" +{ordered.Length - 1}" : "");
        return new Suggest(hotId, ordered, pulse);
    }

    public static Snap Build(IdeChkChannel.ProbeCtx ctx, IdeChkChannel.Snap? ecl = null)
    {
        var suggest = SuggestFor(ctx, ecl);
        var index = Builtins().Select(IndexCard).ToArray();
        return new Snap(true, suggest.Pulse, Builtins().Count, suggest, index);
    }

    public static object Handle(
        IdeChkChannel.ProbeCtx ctx,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        IdeChkChannel.Snap? ecl = null)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "index").Trim().ToLowerInvariant();
        var suggest = SuggestFor(ctx, ecl);

        if (op is "index" or "list" or "catalog" or "scene")
            return Board(null, suggest, action: null, mode: "index");

        if (op is "shelf" or "section")
        {
            var shelf = (Opt(merged, "shelf") ?? Opt(merged, "section") ?? Opt(merged, "id") ?? "").Trim();
            var pages = Builtins()
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

        return Err("unknown_op", "op=index|open|search|shelf|related");
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
            index = Builtins().Select(IndexCard).ToArray(),
            suggest = SuggestCard(suggest),
            shelves = new[] { "systems", "abnormal", "emergency" },
            action,
            hint = "CCL: qrh | qrh open dap-pdb-lock | qrh search pdb | qrh shelf emergency"
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
        Builtins().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

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

        return Builtins().Where(Match).ToArray();
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
