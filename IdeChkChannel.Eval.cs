#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeChkChannel
{
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
            "task.open" or "task" => ctx.TaskOpen,
            "task.none" or "task.closed" => !ctx.TaskOpen,
            "ignite.idle" or "ignite.clear" or "ignite.parked" => ctx.IgniteIdle,
            "ignite.armed" or "ignite.live" => !ctx.IgniteIdle,
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
}
