#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdePluginsChannel
{
    static object BuildListBoard(Snap snap, object? action)
    {
        var lines = snap.Plugins
            .Take(24)
            .Select((p, i) =>
            {
                var mark = p.Attention
                    ? (string.Equals(p.Mode, "A", StringComparison.OrdinalIgnoreCase) ? "A"
                        : string.Equals(p.Mode, "B", StringComparison.OrdinalIgnoreCase) ? "B"
                        : "·")
                    : "×";
                var groups = p.Groups.Count == 0 ? "" : " · " + string.Join(",", p.Groups.Take(3));
                return $"{mark} g{i + 1} {p.DisplayName} {p.Version}{groups}";
            })
            .ToArray();
        if (lines.Length == 0)
            lines = ["(empty — plugins search q=… / enable group=…)"];

        return new
        {
            ok = snap.Ok && ActionOk(action),
            schema = SchemaVersion,
            role = "plugins",
            go = "plugins",
            detail = snap.ShowAll ? "list_all" : "list",
            pulse = snap.Pulse,
            root = CdpPluginQuarantine.Root,
            counts = new
            {
                attention = snap.Count,
                mode_a = snap.ModeA,
                hidden = snap.Hidden,
                listed = snap.Plugins.Count
            },
            view = new { schema = SchemaVersion, lines },
            rows = snap.Plugins.Select((p, i) => new
            {
                id = $"g{i + 1}",
                plugin_id = p.Id,
                display_name = p.DisplayName,
                version = p.Version,
                mode = p.Mode,
                enabled = p.Enabled,
                attention = p.Attention,
                groups = p.Groups,
                payload = p.PayloadPath,
                payload_kind = p.PayloadKind,
                jar = p.JarPath,
                root = p.RootDir,
                go = "plugins",
                go_args = p.Attention
                    ? new { op = "preview", row = $"g{i + 1}" }
                    : new { op = "enable", row = $"g{i + 1}" }
            }).ToArray(),
            action,
            next = BuildNext(snap),
            hint = snap.Count == 0 && snap.Hidden == 0
                ? "Search: plugins search q=plantuml. Install: plugins install id=."
                : "Groups: plugins groups. Kill noise: plugins disable group javascript. Show hidden: all=true."
        };
    }

    static object BuildGroupsBoard(object? action)
    {
        var groups = CdpPluginQuarantine.ListGroups();
        var lines = groups
            .Take(24)
            .Select((g, i) =>
                $"{(g.Enabled ? "·" : "×")} G{i + 1} {g.Id} — {g.Label} ({g.AttentionMembers}/{g.Members})")
            .ToArray();
        if (lines.Length == 0)
            lines = ["(no groups — install a plugin)"];

        var on = groups.Count(g => g.Enabled);
        return new
        {
            ok = ActionOk(action),
            schema = SchemaVersion,
            role = "plugins",
            go = "plugins",
            detail = "groups",
            pulse = $"plugin groups · {on}/{groups.Count} on",
            root = CdpPluginQuarantine.Root,
            counts = new { groups = groups.Count, enabled = on },
            view = new { schema = SchemaVersion, lines },
            rows = groups.Select((g, i) => new
            {
                id = $"G{i + 1}",
                group_id = g.Id,
                label = g.Label,
                enabled = g.Enabled,
                members = g.Members,
                attention_members = g.AttentionMembers,
                go = "plugins",
                go_args = g.Enabled
                    ? new { op = "disable", group = g.Id }
                    : new { op = "enable", group = g.Id }
            }).ToArray(),
            action,
            next = new object[]
            {
                new { go = "plugins", label = "Attention list", why = "op=list" },
                new { go = "plugins", label = "Show all", why = "op=list all=true" },
                new { go = "plugins", label = "Search", why = "op=search q=" }
            },
            hint = "plugins disable group javascript — whole stack off attention. plugins group add id=… group=work"
        };
    }

    static object DoEnableDisable(Dictionary<string, JsonElement> merged, bool enable)
    {
        var group = Opt(merged, "group") ?? Opt(merged, "grp");
        var id = Opt(merged, "id") ?? Opt(merged, "extension") ?? Opt(merged, "plugin");
        var row = Opt(merged, "row") ?? Opt(merged, "pick");

        if (group is { Length: > 0 })
        {
            var r = CdpPluginQuarantine.SetGroupEnabled(group, enable);
            return new
            {
                ok = r.Ok,
                op = enable ? "enable" : "disable",
                target = "group",
                error = r.Error,
                hint = r.Hint,
                group = r.Group is null
                    ? null
                    : new { id = r.Group.Id, label = r.Group.Label, enabled = r.Group.Enabled, members = r.Group.Members }
            };
        }

        var key = id ?? row;
        if (key is null or { Length: 0 })
        {
            return new
            {
                ok = false,
                op = enable ? "enable" : "disable",
                error = "target_required",
                hint = "group=javascript | id=publisher.name | row=g1"
            };
        }

        var pr = CdpPluginQuarantine.SetPluginEnabled(key, enable);
        return new
        {
            ok = pr.Ok,
            op = enable ? "enable" : "disable",
            target = "plugin",
            error = pr.Error,
            hint = pr.Hint,
            plugin = pr.Plugin is null
                ? null
                : new
                {
                    id = pr.Plugin.Id,
                    enabled = pr.Plugin.Enabled,
                    attention = pr.Plugin.Attention,
                    groups = pr.Plugin.Groups
                }
        };
    }

    static object DoGroupAssign(Dictionary<string, JsonElement> merged)
    {
        var sub = (Opt(merged, "sub") ?? Opt(merged, "action") ?? "add").Trim().ToLowerInvariant();
        var group = Opt(merged, "group") ?? Opt(merged, "grp") ?? Opt(merged, "to");
        var id = Opt(merged, "id") ?? Opt(merged, "plugin") ?? Opt(merged, "row");
        if (group is null or { Length: 0 } || id is null or { Length: 0 })
        {
            return new
            {
                ok = false,
                op = "group",
                error = "group_and_id_required",
                hint = "op=group sub=add id=jebbs.plantuml group=work"
            };
        }

        var r = sub is "remove" or "rm" or "del"
            ? CdpPluginQuarantine.RemoveFromGroup(id, group)
            : CdpPluginQuarantine.AddToGroup(id, group);
        return new
        {
            ok = r.Ok,
            op = "group",
            sub,
            error = r.Error,
            hint = r.Hint,
            plugin = r.Plugin is null
                ? null
                : new { id = r.Plugin.Id, groups = r.Plugin.Groups, attention = r.Plugin.Attention }
        };
    }

    static object? DoGroupsAction(Dictionary<string, JsonElement> merged)
    {
        // optional enable/disable via groups board args
        if (Opt(merged, "group") is { Length: > 0 } g
            && (Opt(merged, "enable") is not null || Opt(merged, "disable") is not null || Flag(merged, "enable") || Flag(merged, "disable")))
        {
            var enable = Flag(merged, "enable") || Opt(merged, "enable") is "true" or "1" or "on";
            if (Flag(merged, "disable") || Opt(merged, "disable") is "true" or "1" or "on")
                enable = false;
            return DoEnableDisable(new Dictionary<string, JsonElement>(merged) { ["group"] = JsonSerializer.SerializeToElement(g) }, enable);
        }

        return null;
    }

    static object[] BuildNext(Snap snap)
    {
        var list = new List<object>
        {
            new { go = "plugins", label = "Groups", why = "op=groups — disable whole stacks" },
            new { go = "plugins", label = "Search Open VSX", why = "op=search q=" },
            new { go = "plugins", label = "Refresh", why = "list attention" }
        };
        if (snap.Hidden > 0)
            list.Insert(0, new { go = "plugins", label = "Show hidden", why = "op=list all=true" });
        if (snap.ModeA > 0)
        {
            list.Insert(0, new
            {
                go = "plugins",
                label = "Preview",
                why = "go_args.op=preview — warm .puml"
            });
        }

        list.Add(new { go = "buffer_scene", label = "Buffers", why = "open .puml" });
        return list.ToArray();
    }

    static bool Flag(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => el.GetString() is "1" or "true" or "yes" or "on" or "all",
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            _ => false
        };
    }

}
