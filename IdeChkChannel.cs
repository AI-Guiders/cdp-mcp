#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=ecl</c> (alias <c>chk</c>) — Electronic Checklist / ECL (Boeing-style),
/// Memory Items + AUTO/DO/CONFIRM; catalog customize (add/remove/link).
/// Overlay: <c>ecl.overlay</c> (fallback <c>chk.overlay</c>); acks: <c>ecl.acks</c>.
/// </summary>
internal static class IdeChkChannel
{
    public const string SchemaVersion = "ecl_organ/v1";
    public const string OverlayKey = "ecl.overlay";
    public const string AcksKey = "ecl.acks";
    const string LegacyOverlayKey = "chk.overlay";
    const string LegacyAcksKey = "chk.acks";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public sealed record ProbeCtx(
        bool ProjectOpen,
        bool GitKnown,
        bool GitDirty,
        bool TestsGreen,
        bool TestsFailed,
        bool ProblemsClean,
        bool DapStopped,
        bool DapActive,
        bool SniperOk,
        string Phase,
        string? Intent);

    public sealed record ItemDef(
        string Id,
        string Kind,
        string Text,
        string? Probe = null,
        string? Action = null,
        bool Required = true);

    public sealed record ChecklistDef(
        string Id,
        string Title,
        IReadOnlyList<string> Links,
        IReadOnlyList<ItemDef> MemoryItems,
        IReadOnlyList<ItemDef> Items,
        bool Builtin = true,
        bool Enabled = true);

    public sealed record ItemSnap(
        string Id,
        string Kind,
        string Text,
        bool Done,
        bool Required,
        string? Probe,
        string? Action,
        bool Acked);

    public sealed record RunSnap(
        string Id,
        string Title,
        IReadOnlyList<string> Links,
        bool Builtin,
        bool Enabled,
        bool Active,
        int Done,
        int Total,
        int OpenRequired,
        IReadOnlyList<ItemSnap> MemoryItems,
        IReadOnlyList<ItemSnap> Items);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int ActiveCount,
        int OpenRequired,
        string? HotId,
        IReadOnlyList<RunSnap> Active,
        IReadOnlyList<ChecklistDef> Catalog);

    public static IReadOnlyList<ChecklistDef> Builtins() =>
    [
        new(
            "intake",
            "Before explore",
            ["phase:explore", "phase:clarify", "phase:recall"],
            [
                new("what-why", "memory", "Name what+why (or ask) before thrash", Action: "procedure:intake-brief-plan", Required: false),
                new("find-desk", "memory", "Search via desk (cdp_search / buffer find / index) — not shell/Cursor Grep", Action: "cdp_search", Required: false)
            ],
            [
                new("project", "auto", "cdp_open / project rooted", Probe: "project.open", Action: "cdp_open"),
                new("route", "do", "Route/handoff if deep topic", Action: "memory_session_route_context", Required: false)
            ]),
        new(
            "mutate",
            "Before edit",
            ["phase:act"],
            [
                new("buffer", "memory", "Mutate via cdp_buffer — not Cursor Write", Action: "cdp_buffer", Required: false),
                new("find-desk", "memory", "Search via desk (cdp_search / buffer find / index) — not shell rg", Action: "cdp_search", Required: false),
                new("dap-rebuild", "memory", "debug_stop before rebuild if DAP holds PDB", Action: "cdp_debug", Probe: "dap.not_stopped", Required: false)
            ],
            [
                new("project", "auto", "Project open", Probe: "project.open", Action: "cdp_open"),
                new("sniper", "do", "Aim sniper on large files", Probe: "sniper.ok", Action: "go=scope", Required: false)
            ]),
        new(
            "verify",
            "After verify",
            ["phase:verify"],
            [
                new("tests-desk", "memory", "Tests via desk (cdp_test_scene/cdp_test) — not shell dotnet test", Action: "cdp_test_scene", Required: false)
            ],
            [
                new("problems", "auto", "Problems: no errors", Probe: "problems.clean", Action: "go=problems"),
                new("tests", "auto", "Tests not failing (or not run yet)", Probe: "tests.not_failed", Action: "cdp_test"),
                new("evidence", "do", "Claims have evidence/VC", Action: "go=report", Required: false),
                new("to-review", "do", "Then phase=review (judgment)", Action: "cdp_context", Required: false)
            ]),
        new(
            "review",
            "After review",
            ["phase:review"],
            [
                new("intent-match", "memory", "Diff matches what was asked", Required: false),
                new("blast", "memory", "Blast radius / callers considered", Required: false),
                new("scm-desk", "memory", "SCM via desk (git_scene/git_plan) — not shell status/diff/log", Action: "git_scene", Required: false),
                new("tests-desk", "memory", "Tests via desk (cdp_test_scene/cdp_test) — not shell", Action: "cdp_test_scene", Required: false)
            ],
            [
                new("board", "do", "Open review board (file cards)", Action: "go=review"),
                new("problems", "auto", "Problems still clean", Probe: "problems.clean", Action: "go=problems"),
                new("tests", "auto", "Tests not failing", Probe: "tests.not_failed", Action: "cdp_test"),
                new("slices", "do", "Logical commit slices named", Action: "git_plan", Required: false)
            ]),
        new(
            "ship",
            "Ship (commit/push)",
            ["phase:handoff", "intent:ship"],
            [
                new("secrets", "memory", "No secrets/.env in commit slices", Action: "git_preflight", Required: false),
                new("scm-desk", "memory", "SCM via desk (git_scene/git_plan) — not shell archaeology", Action: "git_scene", Required: false)
            ],
            [
                new("git-known", "auto", "Git scene available", Probe: "git.known", Action: "git_scene"),
                new("commits", "do", "Logical commits (git_plan)", Probe: "git.clean", Action: "git_plan"),
                new("push", "allow", "Standing allow — push after ship commits (ecl unack ship push to revoke)", Action: "git_push", Required: false)
            ]),
        new(
            "dap-hold",
            "DAP stopped",
            ["state:dap.stopped"],
            [
                new("stop-before-rebuild", "memory", "debug_stop before rebuild (PDB lock)", Action: "cdp_debug", Required: false)
            ],
            [
                new("stop-context", "do", "stop_context before guessing", Action: "cdp_debug", Required: false)
            ])
    ];

    public static Snap Build(ProbeCtx ctx, bool catalogOnly = false)
    {
        var catalog = EffectiveCatalog();
        var acks = LoadAcks();
        var runs = new List<RunSnap>();
        foreach (var def in catalog.Where(c => c.Enabled))
        {
            var active = catalogOnly || MatchesAny(def.Links, ctx);
            if (!catalogOnly && !active)
                continue;
            runs.Add(Evaluate(def, ctx, acks, active));
        }

        if (catalogOnly)
        {
            var enabled = catalog.Count(c => c.Enabled);
            return new Snap(
                true,
                $"ecl · catalog {enabled}/{catalog.Count}",
                0,
                0,
                null,
                runs,
                catalog);
        }

        var activeRuns = runs.Where(r => r.Active).ToList();
        var openReq = activeRuns.Sum(r => r.OpenRequired);
        var hot = activeRuns
            .OrderByDescending(r => r.OpenRequired)
            .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var pulse = activeRuns.Count == 0
            ? "ecl · idle"
            : openReq == 0
                ? $"ecl · {activeRuns.Count} clear"
                : hot is null
                    ? $"ecl · open×{openReq}"
                    : $"ecl · {hot.Id} {hot.Done}/{hot.Total} (open×{openReq})";

        return new Snap(true, pulse, activeRuns.Count, openReq, hot?.Id, activeRuns, catalog);
    }

    public static object Handle(
        ProbeCtx ctx,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "run").Trim().ToLowerInvariant();

        object? action = null;
        switch (op)
        {
            case "list" or "catalog":
                return Board(Build(ctx, catalogOnly: true), action, "catalog");
            case "add":
                action = DoAdd(merged);
                break;
            case "remove" or "rm" or "delete":
                action = DoRemove(merged);
                break;
            case "link":
                action = DoLink(merged, add: true);
                break;
            case "unlink":
                action = DoLink(merged, add: false);
                break;
            case "enable" or "on":
                action = DoEnable(merged, enable: true);
                break;
            case "disable" or "off":
                action = DoEnable(merged, enable: false);
                break;
            case "ack" or "done" or "check":
                action = DoAck(merged);
                break;
            case "unack" or "undo":
                action = DoAck(merged, unack: true);
                break;
            case "reset":
                action = DoReset(merged);
                break;
            case "run" or "active" or "scene":
                break;
            default:
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    go = "ecl",
                    error = "unknown_op",
                    hint = "op=run|list|add|remove|link|unlink|enable|disable|ack|reset"
                };
        }

        var snap = Build(ctx);
        return Board(snap, action, "run");
    }

    public static ProbeCtx CtxFrom(
        SessionContext session,
        bool gitKnown,
        bool gitDirty,
        bool testsGreen,
        bool testsFailed,
        bool problemsClean,
        bool dapStopped,
        bool dapActive,
        bool sniperOk)
    {
        var intent = session.Intent is { } i ? CdpEnumParse.ToWire(i) : null;
        return new ProbeCtx(
            !string.IsNullOrWhiteSpace(session.ProjectRoot),
            gitKnown,
            gitDirty,
            testsGreen,
            testsFailed,
            problemsClean,
            dapStopped,
            dapActive,
            sniperOk,
            CdpEnumParse.ToWire(session.Phase),
            intent);
    }

    static object Board(Snap snap, object? action, string mode) => new
    {
        ok = snap.Ok,
        go = "ecl",
        schema = SchemaVersion,
        mode,
        pulse = snap.Pulse,
        title = "ECL",
        note = "Electronic Checklist (ECL) — Memory first, AUTO probes, DO/CONFIRM via ack, ALLOW = standing. Alias: go=chk.",
        active_count = snap.ActiveCount,
        open_required = snap.OpenRequired,
        hot = snap.HotId,
        runs = snap.Active.Select(RunCard).ToArray(),
        catalog = snap.Catalog.Select(c => new
        {
            id = c.Id,
            title = c.Title,
            links = c.Links,
            builtin = c.Builtin,
            enabled = c.Enabled,
            memory = c.MemoryItems.Count,
            items = c.Items.Count
        }).ToArray(),
        action,
        hint = "CCL: ecl | ecl list | ecl link ship phase:verify | ecl ack ship push | ecl add id=… link=phase:act (alias chk)"
    };

    static object RunCard(RunSnap r) => new
    {
        id = r.Id,
        title = r.Title,
        links = r.Links,
        builtin = r.Builtin,
        enabled = r.Enabled,
        active = r.Active,
        done = r.Done,
        total = r.Total,
        open_required = r.OpenRequired,
        memory_items = r.MemoryItems.Select(ItemCard).ToArray(),
        items = r.Items.Select(ItemCard).ToArray()
    };

    static object ItemCard(ItemSnap i) => new
    {
        id = i.Id,
        kind = i.Kind,
        text = i.Text,
        done = i.Done,
        required = i.Required,
        probe = i.Probe,
        action = i.Action,
        acked = i.Acked
    };

    static RunSnap Evaluate(ChecklistDef def, ProbeCtx ctx, HashSet<string> acks, bool active)
    {
        var mem = def.MemoryItems.Select(i => EvalItem(def.Id, i, ctx, acks)).ToList();
        var items = def.Items.Select(i => EvalItem(def.Id, i, ctx, acks)).ToList();
        var all = mem.Concat(items).ToList();
        var done = all.Count(i => i.Done);
        var total = all.Count;
        var openReq = all.Count(i => i.Required && !i.Done);
        return new RunSnap(
            def.Id, def.Title, def.Links, def.Builtin, def.Enabled, active,
            done, total, openReq, mem, items);
    }

    static ItemSnap EvalItem(string checklistId, ItemDef def, ProbeCtx ctx, HashSet<string> acks)
    {
        var key = AckKey(checklistId, def.Id);
        var acked = acks.Contains(key);
        var probed = def.Probe is { Length: > 0 } && Probe(def.Probe, ctx);
        // Memory with probe (e.g. dap.not_stopped): auto-clear when safe; else need ack.
        // allow = standing operator consent (always clear; revoke by changing kind / overlay).
        var done = def.Kind.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? probed || (def.Probe is null or { Length: 0 } && acked)
            : def.Kind.Equals("allow", StringComparison.OrdinalIgnoreCase)
                ? true
                : acked || probed;
        return new ItemSnap(def.Id, def.Kind, def.Text, done, def.Required, def.Probe, def.Action, acked);
    }

    public static bool Probe(string probe, ProbeCtx ctx) =>
        probe.Trim().ToLowerInvariant() switch
        {
            "project.open" or "project" => ctx.ProjectOpen,
            "git.known" or "git" => ctx.GitKnown,
            "git.clean" => !ctx.GitDirty,
            "git.dirty" => ctx.GitDirty,
            "tests.green" => ctx.TestsGreen,
            "tests.not_failed" => !ctx.TestsFailed,
            "problems.clean" or "problems" => ctx.ProblemsClean,
            "dap.stopped" => ctx.DapStopped,
            "dap.not_stopped" or "dap.idle" => !ctx.DapStopped,
            "dap.active" => ctx.DapActive,
            "sniper.ok" => ctx.SniperOk,
            "always" => true,
            "never" => false,
            _ => false
        };

    public static bool MatchesAny(IReadOnlyList<string> links, ProbeCtx ctx)
    {
        if (links.Count == 0)
            return false;
        return links.Any(l => MatchLink(l, ctx));
    }

    public static bool MatchLink(string link, ProbeCtx ctx)
    {
        if (string.IsNullOrWhiteSpace(link))
            return false;
        var parts = link.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(p => MatchAtom(p, ctx));
    }

    static bool MatchAtom(string atom, ProbeCtx ctx)
    {
        var s = atom.Trim();
        if (s.Equals("always", StringComparison.OrdinalIgnoreCase))
            return true;

        var colon = s.IndexOf(':');
        if (colon <= 0)
            return false;
        var kind = s[..colon].Trim().ToLowerInvariant();
        var value = s[(colon + 1)..].Trim();
        return kind switch
        {
            "phase" => ctx.Phase.Equals(value, StringComparison.OrdinalIgnoreCase),
            "intent" => ctx.Intent is { } i && i.Equals(value, StringComparison.OrdinalIgnoreCase),
            "state" => Probe(value, ctx),
            "object" => true, // soft — object filter later
            _ => false
        };
    }

    public static IReadOnlyList<ChecklistDef> EffectiveCatalog()
    {
        var overlay = LoadOverlay();
        var map = new Dictionary<string, ChecklistDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in Builtins())
            map[b.Id] = b;

        foreach (var id in overlay.Removed ?? [])
            map.Remove(id);

        foreach (var c in overlay.Custom ?? [])
        {
            if (c.Id is not { Length: > 0 })
                continue;
            map[c.Id] = ToDef(c, builtin: false);
        }

        foreach (var (id, links) in overlay.ExtraLinks ?? new Dictionary<string, List<string>>())
        {
            if (!map.TryGetValue(id, out var cur))
                continue;
            var merged = cur.Links.Concat(links ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            map[id] = cur with { Links = merged };
        }

        foreach (var (id, links) in overlay.RemovedLinks ?? new Dictionary<string, List<string>>())
        {
            if (!map.TryGetValue(id, out var cur))
                continue;
            var drop = new HashSet<string>(links ?? [], StringComparer.OrdinalIgnoreCase);
            map[id] = cur with { Links = cur.Links.Where(l => !drop.Contains(l)).ToArray() };
        }

        foreach (var id in overlay.Disabled ?? [])
        {
            if (!map.TryGetValue(id, out var cur))
                continue;
            map[id] = cur with { Enabled = false };
        }

        foreach (var id in overlay.Enabled ?? [])
        {
            if (!map.TryGetValue(id, out var cur))
                continue;
            map[id] = cur with { Enabled = true };
        }

        return map.Values.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static object DoAdd(Dictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "name");
        if (id is not { Length: > 0 })
            return Err("id_required", "ecl add id=mine title=… link=phase:act");
        id = SanitizeId(id);
        var title = Opt(args, "title") ?? id;
        var links = ParseLinks(args);
        if (links.Count == 0)
            return Err("link_required", "ecl add … link=phase:act (or links=phase:a,intent:ship)");

        var overlay = LoadOverlay();
        overlay.Custom ??= [];
        overlay.Removed?.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        overlay.Custom.RemoveAll(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        var items = new List<OverlayItem>();
        if (Opt(args, "item") is { Length: > 0 } itemText)
        {
            items.Add(new OverlayItem
            {
                Id = "item1",
                Kind = Opt(args, "kind") ?? "do",
                Text = itemText,
                Action = Opt(args, "action"),
                Probe = Opt(args, "probe"),
                Required = !Flag(args, "optional")
            });
        }

        overlay.Custom.Add(new OverlayChecklist
        {
            Id = id,
            Title = title,
            Links = links,
            MemoryItems = [],
            Items = items,
            Enabled = true
        });
        SaveOverlay(overlay);
        return new { ok = true, op = "add", id, links, title };
    }

    static object DoRemove(Dictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "name");
        if (id is not { Length: > 0 })
            return Err("id_required", "ecl remove id=ship");

        var overlay = LoadOverlay();
        var customHit = overlay.Custom?.RemoveAll(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (Builtins().Any(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            overlay.Removed ??= [];
            if (!overlay.Removed.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                overlay.Removed.Add(id);
        }
        else if (!customHit)
            return Err("not_found", $"checklist '{id}' not in catalog");

        overlay.ExtraLinks?.Remove(id);
        overlay.RemovedLinks?.Remove(id);
        SaveOverlay(overlay);
        return new { ok = true, op = "remove", id };
    }

    static object DoLink(Dictionary<string, JsonElement> args, bool add)
    {
        var id = Opt(args, "id") ?? Opt(args, "name") ?? Opt(args, "checklist");
        var link = Opt(args, "link") ?? Opt(args, "to") ?? Opt(args, "on");
        if (id is not { Length: > 0 } || link is not { Length: > 0 })
            return Err("id_link_required", "ecl link ship phase:handoff | ecl unlink ship intent:ship");

        link = NormalizeLink(link);
        var overlay = LoadOverlay();
        if (add)
        {
            overlay.ExtraLinks ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!overlay.ExtraLinks.TryGetValue(id, out var list))
            {
                list = [];
                overlay.ExtraLinks[id] = list;
            }

            if (!list.Any(x => x.Equals(link, StringComparison.OrdinalIgnoreCase)))
                list.Add(link);
            overlay.RemovedLinks?.GetValueOrDefault(id)?.RemoveAll(x => x.Equals(link, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            overlay.ExtraLinks?.GetValueOrDefault(id)?.RemoveAll(x => x.Equals(link, StringComparison.OrdinalIgnoreCase));
            // Builtin link removal via RemovedLinks
            overlay.RemovedLinks ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!overlay.RemovedLinks.TryGetValue(id, out var drop))
            {
                drop = [];
                overlay.RemovedLinks[id] = drop;
            }

            if (!drop.Any(x => x.Equals(link, StringComparison.OrdinalIgnoreCase)))
                drop.Add(link);
        }

        SaveOverlay(overlay);
        return new { ok = true, op = add ? "link" : "unlink", id, link };
    }

    static object DoEnable(Dictionary<string, JsonElement> args, bool enable)
    {
        var id = Opt(args, "id") ?? Opt(args, "name");
        if (id is not { Length: > 0 })
            return Err("id_required", "ecl enable id=ship");

        var overlay = LoadOverlay();
        if (enable)
        {
            overlay.Disabled?.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            overlay.Enabled ??= [];
            if (!overlay.Enabled.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                overlay.Enabled.Add(id);
            if (overlay.Custom is { } customs)
            {
                var ix = customs.FindIndex(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (ix >= 0)
                    customs[ix].Enabled = true;
            }
        }
        else
        {
            overlay.Enabled?.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            overlay.Disabled ??= [];
            if (!overlay.Disabled.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                overlay.Disabled.Add(id);
            if (overlay.Custom is { } customs)
            {
                var ix = customs.FindIndex(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (ix >= 0)
                    customs[ix].Enabled = false;
            }
        }

        SaveOverlay(overlay);
        return new { ok = true, op = enable ? "enable" : "disable", id };
    }

    static object DoAck(Dictionary<string, JsonElement> args, bool unack = false)
    {
        var checklist = Opt(args, "checklist") ?? Opt(args, "id") ?? Opt(args, "name");
        var item = Opt(args, "item") ?? Opt(args, "step");
        // Allow "chk ack ship push" style via positional: checklist + item already in id/item
        if (item is null && Opt(args, "arg1") is { } a1 && Opt(args, "arg0") is { } a0)
        {
            checklist = a0;
            item = a1;
        }

        if (checklist is not { Length: > 0 } || item is not { Length: > 0 })
            return Err("checklist_item_required", "ecl ack ship push");

        var acks = LoadAcks();
        var key = AckKey(checklist, item);
        if (unack)
            acks.Remove(key);
        else
            acks.Add(key);
        SaveAcks(acks);
        return new { ok = true, op = unack ? "unack" : "ack", checklist, item, key };
    }

    static object DoReset(Dictionary<string, JsonElement> args)
    {
        var what = (Opt(args, "what") ?? Opt(args, "scope") ?? "overlay").Trim().ToLowerInvariant();
        if (what is "acks" or "ack")
        {
            IdeSettingsStore.Unset(AcksKey);
            IdeSettingsStore.Unset(LegacyAcksKey);
            return new { ok = true, op = "reset", what = "acks" };
        }

        if (what is "all")
        {
            IdeSettingsStore.Unset(OverlayKey);
            IdeSettingsStore.Unset(LegacyOverlayKey);
            IdeSettingsStore.Unset(AcksKey);
            IdeSettingsStore.Unset(LegacyAcksKey);
            return new { ok = true, op = "reset", what = "all" };
        }

        IdeSettingsStore.Unset(OverlayKey);
        IdeSettingsStore.Unset(LegacyOverlayKey);
        return new { ok = true, op = "reset", what = "overlay" };
    }

    static string AckKey(string checklistId, string itemId) =>
        $"{checklistId.Trim()}:{itemId.Trim()}".ToLowerInvariant();

    static List<string> ParseLinks(Dictionary<string, JsonElement> args)
    {
        var list = new List<string>();
        void AddOne(string? raw)
        {
            if (raw is not { Length: > 0 })
                return;
            foreach (var part in raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var n = NormalizeLink(part);
                if (n.Length > 0 && !list.Contains(n, StringComparer.OrdinalIgnoreCase))
                    list.Add(n);
            }
        }

        AddOne(Opt(args, "link"));
        AddOne(Opt(args, "links"));
        AddOne(Opt(args, "on"));
        return list;
    }

    static string NormalizeLink(string link)
    {
        var s = link.Trim();
        // Allow bare "handoff" → phase:handoff when known phase/intent/state tokens
        if (!s.Contains(':', StringComparison.Ordinal))
        {
            var low = s.ToLowerInvariant();
            if (low is "explore" or "clarify" or "recall" or "plan" or "act" or "verify" or "review" or "handoff")
                return "phase:" + low;
            if (low is "ship" or "fix" or "deploy")
                return "intent:" + low;
            if (low.StartsWith("git.", StringComparison.Ordinal) || low.StartsWith("dap.", StringComparison.Ordinal)
                || low is "always")
                return low == "always" ? "always" : "state:" + low;
        }

        return s;
    }

    static string SanitizeId(string id)
    {
        var chars = id.Trim().ToLowerInvariant().Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    static ChecklistDef ToDef(OverlayChecklist c, bool builtin) =>
        new(
            c.Id,
            c.Title ?? c.Id,
            c.Links ?? [],
            (c.MemoryItems ?? []).Select(ToItem).ToArray(),
            (c.Items ?? []).Select(ToItem).ToArray(),
            builtin,
            c.Enabled);

    static ItemDef ToItem(OverlayItem i) =>
        new(i.Id, i.Kind ?? "do", i.Text ?? i.Id, i.Probe, i.Action, i.Required);

    static OverlayDoc LoadOverlay()
    {
        var raw = IdeSettingsStore.GetOrNull(OverlayKey)
                  ?? IdeSettingsStore.GetOrNull(LegacyOverlayKey);
        if (raw is not { Length: > 0 })
            return new OverlayDoc();
        try
        {
            return JsonSerializer.Deserialize<OverlayDoc>(raw, JsonOpts) ?? new OverlayDoc();
        }
        catch
        {
            return new OverlayDoc();
        }
    }

    static void SaveOverlay(OverlayDoc doc)
    {
        IdeSettingsStore.Set(OverlayKey, JsonSerializer.Serialize(doc, JsonOpts));
        IdeSettingsStore.Unset(LegacyOverlayKey);
    }

    static HashSet<string> LoadAcks()
    {
        var raw = IdeSettingsStore.GetOrNull(AcksKey)
                  ?? IdeSettingsStore.GetOrNull(LegacyAcksKey);
        if (raw is not { Length: > 0 })
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(raw, JsonOpts) ?? [];
            return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    static void SaveAcks(HashSet<string> acks)
    {
        IdeSettingsStore.Set(AcksKey, JsonSerializer.Serialize(acks.OrderBy(x => x).ToList(), JsonOpts));
        IdeSettingsStore.Unset(LegacyAcksKey);
    }

    static object Err(string error, string hint) => new { ok = false, error, hint };

    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null)
            return d;
        foreach (var kv in args)
            d[kv.Key] = kv.Value;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
                d[p.Name] = p.Value.Clone();
        }

        return d;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static bool Flag(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b,
            _ => false
        };
    }

    sealed class OverlayDoc
    {
        public List<string>? Removed { get; set; }
        public List<string>? Disabled { get; set; }
        public List<string>? Enabled { get; set; }
        public Dictionary<string, List<string>>? ExtraLinks { get; set; }
        public Dictionary<string, List<string>>? RemovedLinks { get; set; }
        public List<OverlayChecklist>? Custom { get; set; }
    }

    sealed class OverlayChecklist
    {
        public string Id { get; set; } = "";
        public string? Title { get; set; }
        public List<string>? Links { get; set; }
        public List<OverlayItem>? MemoryItems { get; set; }
        public List<OverlayItem>? Items { get; set; }
        public bool Enabled { get; set; } = true;
    }

    sealed class OverlayItem
    {
        public string Id { get; set; } = "";
        public string? Kind { get; set; }
        public string? Text { get; set; }
        public string? Probe { get; set; }
        public string? Action { get; set; }
        public bool Required { get; set; } = true;
    }
}
