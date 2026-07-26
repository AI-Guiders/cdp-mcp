#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=plugins</c> — Open VSX search/install + groups (auto+manual) + attention enable/disable.
/// </summary>
internal static class IdePluginsChannel
{
    public const string SchemaVersion = "plugins_channel/v1";

    static readonly object SearchGate = new();
    static IReadOnlyList<OpenVsxClient.Hit> LastSearchHits = [];

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int Count,
        int ModeA,
        int Hidden,
        IReadOnlyList<CdpPluginQuarantine.PluginInfo> Plugins,
        bool ShowAll);

    public static Snap Build(bool showAll = false)
    {
        var attention = CdpPluginQuarantine.List(attentionOnly: true);
        var all = showAll ? CdpPluginQuarantine.List(attentionOnly: false) : attention;
        var hidden = showAll
            ? all.Count(p => !p.Attention)
            : CdpPluginQuarantine.List(attentionOnly: false).Count - attention.Count;
        var modeA = attention.Count(p => string.Equals(p.Mode, "A", StringComparison.OrdinalIgnoreCase));
        var pulse = attention.Count == 0
            ? (hidden > 0
                ? $"plugins · attention empty ({hidden} off — enable group/plugin)"
                : "plugins · empty — search Open VSX")
            : $"plugins · {attention.Count} attn ({modeA} Mode A)"
              + (hidden > 0 ? $" · {hidden} hidden" : "");
        return new Snap(true, pulse, attention.Count, modeA, Math.Max(0, hidden), all, showAll);
    }

    public static object Handle(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        CancellationToken cancellationToken = default)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "list").Trim().ToLowerInvariant();

        if (op is "list" or "installed"
            && (Opt(merged, "q") ?? Opt(merged, "query")) is { Length: > 0 })
            op = "search";

        object? action = null;
        var showAll = Flag(merged, "all") || Flag(merged, "show_all") || Flag(merged, "hidden");

        if (op is "search" or "find" or "query")
        {
            var search = DoSearch(merged, cancellationToken);
            action = search.Card;
            return BuildSearchBoard(search.Result, action, Build());
        }

        if (op is "groups" or "grouplist")
            return BuildGroupsBoard(DoGroupsAction(merged));

        if (op is "group" or "tag")
        {
            action = DoGroupAssign(merged);
            op = Flag(merged, "list_groups") ? "groups" : "list";
            if (op is "groups")
                return BuildGroupsBoard(action);
        }
        else if (op is "enable" or "on")
        {
            action = DoEnableDisable(merged, enable: true);
            op = "list";
            showAll = true;
        }
        else if (op is "disable" or "off")
        {
            action = DoEnableDisable(merged, enable: false);
            op = "list";
            showAll = true;
        }
        else if (op is "install" or "add")
        {
            action = DoInstall(merged, cancellationToken);
            op = "list";
        }
        else if (op is "want" or "need" or "get")
        {
            return DoWant(merged, cancellationToken);
        }
        else if (op is "reharvest" or "rescan" or "reclassify")
        {
            var n = CdpPluginQuarantine.ReharvestInstalled();
            action = new
            {
                ok = true,
                op = "reharvest",
                updated = n,
                hint = "Intent→Discover→Triage rewritten (delivery/host_deps)"
            };
            op = "list";
            showAll = true;
        }
        else if (op is "preview" or "render" or "png")
        {
            action = DoPreview(store, session, merged, cancellationToken);
        }

        return BuildListBoard(Build(showAll), action);
    }

    public static object PulseCard(Snap snap) => new
    {
        schema = SchemaVersion,
        ok = snap.Ok,
        pulse = snap.Pulse,
        plugins = snap.Count,
        mode_a = snap.ModeA,
        hidden = snap.Hidden
    };

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

    sealed record SearchPack(OpenVsxClient.SearchResult Result, object Card);

    static SearchPack DoSearch(Dictionary<string, JsonElement> merged, CancellationToken cancellationToken)
    {
        var q = Opt(merged, "q") ?? Opt(merged, "query") ?? Opt(merged, "text") ?? "";
        var size = IntOr(merged, "size", OpenVsxClient.DefaultSize);
        var result = OpenVsxClient.Search(q, size, cancellationToken);
        if (result.Ok)
        {
            lock (SearchGate)
            {
                LastSearchHits = result.Hits;
            }
        }

        var card = new
        {
            ok = result.Ok,
            op = "search",
            error = result.Error,
            hint = result.Hint,
            query = result.Query,
            hits = result.Hits.Count
        };
        return new SearchPack(result, card);
    }

    static object BuildSearchBoard(OpenVsxClient.SearchResult result, object action, Snap installed)
    {
        var lines = result.Hits
            .Take(24)
            .Select((h, i) => $"s{i + 1} {h.Id} {h.Version} — {Trunc(h.DisplayName ?? h.Name, 40)}")
            .ToArray();
        if (lines.Length == 0)
            lines = [result.Ok ? "(no hits)" : $"(search failed: {result.Error})"];

        var pulse = result.Ok
            ? $"plugins search · {result.Hits.Count} «{Trunc(result.Query, 24)}»"
            : $"plugins search FAIL · {result.Error}";

        return new
        {
            ok = result.Ok,
            schema = SchemaVersion,
            role = "plugins",
            go = "plugins",
            detail = "search",
            pulse,
            root = CdpPluginQuarantine.Root,
            counts = new
            {
                hits = result.Hits.Count,
                plugins = installed.Count,
                mode_a = installed.ModeA
            },
            view = new { schema = SchemaVersion, lines },
            rows = result.Hits.Select((h, i) => new
            {
                id = $"s{i + 1}",
                plugin_id = "openvsx:" + h.Id,
                display_name = h.DisplayName ?? h.Name,
                version = h.Version,
                description = h.Description,
                ns = h.Namespace,
                name = h.Name,
                go = "plugins",
                go_args = new { op = "install", id = h.Id }
            }).ToArray(),
            action,
            next = new object[]
            {
                new { go = "plugins", label = "Install s1", why = result.Hits.Count > 0 ? "id=" + result.Hits[0].Id : "no hits" },
                new { go = "plugins", label = "Installed", why = "op=list" },
                new { go = "plugins", label = "Search again", why = "op=search q=" }
            },
            hint = result.Ok
                ? "Tap row → op=install id=publisher.name — CDP fetch+unpack. Or cmd: plugins install jebbs.plantuml"
                : (result.Hint ?? "Fix query / network")
        };
    }

    static object DoInstall(Dictionary<string, JsonElement> merged, CancellationToken cancellationToken)
    {
        var path = Opt(merged, "path") ?? Opt(merged, "vsix") ?? Opt(merged, "file");
        var id = Opt(merged, "id") ?? Opt(merged, "extension") ?? Opt(merged, "ext");
        var row = Opt(merged, "row") ?? Opt(merged, "pick");
        var version = Opt(merged, "version") ?? Opt(merged, "ver");

        // row=s1 from last search
        if ((id is null || id.Length == 0) && row is { Length: > 0 }
            && TryResolveSearchRow(row, out var hitId))
            id = hitId;

        if (path is { Length: > 0 })
        {
            path = Path.GetFullPath(path);
            CdpPluginQuarantine.InstallResult local;
            if (Directory.Exists(path))
                local = CdpPluginQuarantine.InstallFromUnpacked(path);
            else if (File.Exists(path))
                local = CdpPluginQuarantine.InstallFromVsix(path);
            else
                return FailInstall("path_not_found", "path= .vsix / extension dir, or id=publisher.name", path);

            return InstallCard(local, source: path);
        }

        if (id is { Length: > 0 })
        {
            if (!OpenVsxClient.TryParseId(id, out var ns, out var name))
            {
                return FailInstall(
                    "id_bad",
                    "Use publisher.name (jebbs.plantuml) or namespace/name",
                    id);
            }

            var dl = OpenVsxClient.Download(ns, name, version, cancellationToken);
            if (!dl.Ok || dl.Path is not { Length: > 0 })
            {
                return new
                {
                    ok = false,
                    op = "install",
                    error = dl.Error ?? "download_failed",
                    hint = dl.Hint ?? "Open VSX resolve/download failed",
                    id = ns + "." + name,
                    version,
                    meta = dl.Meta is null
                        ? null
                        : new { id = dl.Meta.Id, version = dl.Meta.Version, display_name = dl.Meta.DisplayName }
                };
            }

            var installed = CdpPluginQuarantine.InstallFromVsix(dl.Path);
            return new
            {
                ok = installed.Ok,
                op = "install",
                error = installed.Error,
                hint = installed.Hint,
                source = "open-vsx",
                id = ns + "." + name,
                downloaded = dl.Path,
                version = dl.Meta?.Version,
                host = CdpPluginQuarantine.HostProbeCard(installed.Plugin),
                plugin = installed.Plugin is null
                    ? null
                    : new
                    {
                        id = installed.Plugin.Id,
                        display_name = installed.Plugin.DisplayName,
                        version = installed.Plugin.Version,
                        mode = installed.Plugin.Mode,
                        payload = installed.Plugin.PayloadPath,
                        payload_kind = installed.Plugin.PayloadKind,
                        jar = installed.Plugin.JarPath,
                        root = installed.Plugin.RootDir
                    }
            };
        }

        return FailInstall(
            "install_target_required",
            "id=publisher.name (Open VSX) | path=.vsix | row=s1 from last search",
            null);
    }

    static bool TryResolveSearchRow(string row, out string id)
    {
        id = "";
        var k = row.Trim();
        IReadOnlyList<OpenVsxClient.Hit> hits;
        lock (SearchGate) hits = LastSearchHits;
        if (hits.Count == 0)
            return false;

        if (int.TryParse(k, out var n) || (k.StartsWith('s') && int.TryParse(k[1..], out n)))
        {
            if (n >= 1 && n <= hits.Count)
            {
                id = hits[n - 1].Id;
                return true;
            }
        }

        return false;
    }

    static object InstallCard(CdpPluginQuarantine.InstallResult result, string? source)
    {
        var host = CdpPluginQuarantine.HostProbeCard(result.Plugin);
        return new
        {
            ok = result.Ok,
            op = "install",
            error = result.Error,
            hint = result.Hint,
            source,
            host,
            plugin = result.Plugin is null
                ? null
                : new
                {
                    id = result.Plugin.Id,
                    display_name = result.Plugin.DisplayName,
                    version = result.Plugin.Version,
                    mode = result.Plugin.Mode,
                    payload = result.Plugin.PayloadPath,
                    payload_kind = result.Plugin.PayloadKind,
                    jar = result.Plugin.JarPath,
                    root = result.Plugin.RootDir
                }
        };
    }

    /// <summary>Feature intent: search Open VSX → ingest first takeable (A/B).</summary>
    static object DoWant(Dictionary<string, JsonElement> merged, CancellationToken cancellationToken)
    {
        var q = Opt(merged, "q") ?? Opt(merged, "query") ?? Opt(merged, "feature") ?? Opt(merged, "text") ?? "";
        if (q.Length == 0)
        {
            return BuildListBoard(Build(showAll: true), new
            {
                ok = false,
                op = "want",
                error = "feature_required",
                hint = "plugins want plantuml | checkstyle | shellcheck"
            });
        }

        var search = DoSearch(new Dictionary<string, JsonElement>
        {
            ["q"] = JsonSerializer.SerializeToElement(q)
        }, cancellationToken);

        if (!search.Result.Ok || search.Result.Hits.Count == 0)
        {
            return BuildSearchBoard(search.Result, new
            {
                ok = false,
                op = "want",
                error = search.Result.Ok ? "no_hits" : search.Result.Error,
                hint = search.Result.Hint ?? "No Open VSX hits for feature",
                feature = q
            }, Build());
        }

        var tried = new List<object>();
        object? primaryRefuse = null;
        foreach (var hit in search.Result.Hits.Take(5))
        {
            var dl = OpenVsxClient.Download(hit.Namespace, hit.Name, version: null, cancellationToken);
            if (!dl.Ok || dl.Path is not { Length: > 0 })
            {
                tried.Add(new { id = hit.Id, ok = false, error = dl.Error ?? "download_failed" });
                continue;
            }

            var installed = CdpPluginQuarantine.InstallFromVsix(dl.Path);
            var host = CdpPluginQuarantine.HostProbeCard(installed.Plugin);
            var fits = WantHitFitsFeature(hit, q, installed.Plugin);
            var takeable = installed is { Ok: true, Plugin: not null }
                && installed.Plugin.Attention
                && installed.Plugin.Mode is "A" or "B"
                && fits;
            tried.Add(new
            {
                id = hit.Id,
                ok = installed.Ok,
                mode = installed.Plugin?.Mode,
                takeable,
                feature_fit = fits,
                hint = installed.Hint,
                host
            });

            if (takeable)
            {
                return BuildListBoard(Build(showAll: true), new
                {
                    ok = true,
                    op = "want",
                    feature = q,
                    chosen = hit.Id,
                    hint = installed.Hint,
                    host,
                    tried,
                    plugin = new
                    {
                        id = installed.Plugin!.Id,
                        display_name = installed.Plugin.DisplayName,
                        version = installed.Plugin.Version,
                        mode = installed.Plugin.Mode,
                        payload = installed.Plugin.PayloadPath,
                        payload_kind = installed.Plugin.PayloadKind,
                        root = installed.Plugin.RootDir
                    }
                });
            }

            if (primaryRefuse is null
                && installed.Plugin?.Mode is "D"
                && WantHitNameMatch(hit, q))
            {
                primaryRefuse = new
                {
                    id = hit.Id,
                    mode = "D",
                    hint = installed.Hint
                };
            }
        }

        return BuildSearchBoard(search.Result, new
        {
            ok = false,
            op = "want",
            feature = q,
            error = "no_takeable",
            hint = primaryRefuse is not null
                ? "Feature-matched candidate is Mode D — refuse (see tried); not auto-picking tag-only mega extensions"
                : "Candidates found but none Mode A/B takeable for this feature — pick sN or refuse (Mode D)",
            refuse = primaryRefuse,
            tried
        }, Build(showAll: true));
    }

    /// <summary>
    /// Auto-want only when the hit is about the feature — not a kitchen-sink that merely tags it.
    /// </summary>
    internal static bool WantHitFitsFeature(
        OpenVsxClient.Hit hit,
        string featureQuery,
        CdpPluginQuarantine.PluginInfo? plugin)
    {
        if (WantHitNameMatch(hit, featureQuery))
            return true;
        if (plugin is null)
            return false;
        // Mega marketplace packs (Trunk, …) list dozens of tools as tags — never auto-want them
        // just because one tag matches the query.
        if (plugin.Groups.Count > 24)
            return false;
        var ql = featureQuery.Trim();
        return plugin.Groups.Any(g =>
            g.Equals(ql, StringComparison.OrdinalIgnoreCase)
            || g.Contains(ql, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool WantHitNameMatch(OpenVsxClient.Hit hit, string featureQuery)
    {
        var ql = featureQuery.Trim();
        if (ql.Length == 0)
            return false;
        return hit.Name.Contains(ql, StringComparison.OrdinalIgnoreCase)
               || hit.Id.Contains(ql, StringComparison.OrdinalIgnoreCase)
               || (hit.DisplayName?.Contains(ql, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    static object FailInstall(string error, string hint, string? detail) => new
    {
        ok = false,
        op = "install",
        error,
        hint,
        detail
    };

    static object DoPreview(
        DocumentBufferStore store,
        SessionContext session,
        Dictionary<string, JsonElement> merged,
        CancellationToken cancellationToken)
    {
        var path = Opt(merged, "path") ?? Opt(merged, "file");
        string body;
        string? sourcePath;

        if (path is { Length: > 0 })
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
                return new { ok = false, op = "preview", error = "path_not_found", path };
            body = File.ReadAllText(path);
            sourcePath = path;
        }
        else
        {
            var buf = PickPlantBuffer(store, merged);
            if (buf is null)
            {
                return new
                {
                    ok = false,
                    op = "preview",
                    error = "no_plantuml_buffer",
                    hint = "Open/create .puml buffer, or path= to diagram."
                };
            }

            body = buf.Text;
            sourcePath = buf.Path;
        }

        if (!PlantUmlRender.LooksLikePlantUml(body, sourcePath, fence: null))
            return new { ok = false, op = "preview", error = "not_plantuml", path = sourcePath };

        var rendered = PlantUmlRender.RenderPng(body, cancellationToken);
        if (!rendered.Ok || rendered.Png is not { Length: > 0 } png)
        {
            return new
            {
                ok = false,
                op = "preview",
                error = rendered.Error ?? "render_failed",
                jar = rendered.Jar,
                path = sourcePath
            };
        }

        var previewPath = TryWritePreviewPng(sourcePath, session.ProjectRoot, png);
        return new
        {
            ok = true,
            op = "preview",
            kind = "plantuml_png",
            bytes = png.Length,
            mime = "image/png",
            preview_path = previewPath,
            jar = rendered.Jar,
            source = sourcePath,
            note = previewPath is null
                ? "PNG rendered but write failed"
                : "PNG on disk — Read preview_path or take with vision=true"
        };
    }

    static DocBuffer? PickPlantBuffer(DocumentBufferStore store, Dictionary<string, JsonElement> merged)
    {
        var docId = Opt(merged, "doc_id");
        if (docId is { Length: > 0 })
        {
            var hit = store.All.FirstOrDefault(b =>
                string.Equals(b.DocId, docId, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
        }

        return store.All
            .OrderByDescending(b => b.Version)
            .FirstOrDefault(b => PlantUmlRender.LooksLikePlantUml(b.Text, b.Path, fence: null));
    }

    static string? TryWritePreviewPng(string? sourcePath, string? projectRoot, byte[] png)
    {
        try
        {
            string dir;
            string name;
            if (sourcePath is { Length: > 0 })
            {
                dir = Path.GetDirectoryName(sourcePath) ?? projectRoot ?? Path.GetTempPath();
                name = Path.GetFileNameWithoutExtension(sourcePath) + ".png";
            }
            else
            {
                dir = projectRoot is { Length: > 0 }
                    ? Path.Combine(projectRoot, ".cdp", "evidence", "plugins")
                    : Path.Combine(Path.GetTempPath(), "cdp-plugins");
                name = "preview.png";
            }

            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, png);
            return path;
        }
        catch
        {
            return null;
        }
    }

    static bool ActionOk(object? action)
    {
        if (action is null)
            return true;
        try
        {
            var json = JsonSerializer.Serialize(action);
            using var doc = JsonDocument.Parse(json);
            return !doc.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.False;
        }
        catch
        {
            return true;
        }
    }

    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null)
            return merged;

        foreach (var kv in args)
        {
            if (kv.Key is "go_args" && kv.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in kv.Value.EnumerateObject())
                    merged[p.Name] = p.Value.Clone();
                continue;
            }

            merged[kv.Key] = kv.Value.Clone();
        }

        return merged;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            _ => null
        };
    }

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int fallback)
    {
        if (!args.TryGetValue(key, out var el))
            return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => fallback
        };
    }

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
