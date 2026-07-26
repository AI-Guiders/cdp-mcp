#nullable enable
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

/// <summary>
/// Mode-A plugin quarantine under %LocalAppData%/cdp-mcp/plugins/.
/// Groups (auto+manual) + enable/disable — attention filter for agent UX.
/// </summary>
internal static class CdpPluginQuarantine
{
    public const string ManifestFileName = "cdp-plugin.json";
    public const string GroupsFileName = "groups.json";
    public const string SchemaVersion = "cdp_plugin/v1.1";
    public const string GroupsSchema = "cdp_plugin_groups/v0";

    public sealed record PluginInfo(
        string Id,
        string DisplayName,
        string Version,
        string Mode,
        string RootDir,
        string? PayloadPath,
        string? PayloadKind,
        string ManifestPath,
        bool Enabled,
        IReadOnlyList<string> Groups,
        bool Attention)
    {
        /// <summary>Jar payload only (PlantUML preview). Other Mode A kinds use <see cref="PayloadPath"/>.</summary>
        public string? JarPath =>
            PayloadPath is null ? null
            : string.Equals(PayloadKind, "jar", StringComparison.OrdinalIgnoreCase)
              || PayloadPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                ? PayloadPath
                : null;
    }

    public sealed record GroupInfo(
        string Id,
        string Label,
        bool Enabled,
        int Members,
        int AttentionMembers);

    public sealed record InstallResult(
        bool Ok,
        string? Error,
        PluginInfo? Plugin,
        string? Hint);

    public sealed record MutateResult(
        bool Ok,
        string? Error,
        string? Hint,
        PluginInfo? Plugin,
        GroupInfo? Group);

    public static string Root
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("CDP_PLUGINS_ROOT");
            if (env is { Length: > 0 })
                return Path.GetFullPath(env);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cdp-mcp",
                "plugins");
        }
    }

    static string GroupsPath => Path.Combine(Root, GroupsFileName);

    /// <summary>Default: attention only (enabled plugin ∩ enabled groups).</summary>
    public static IReadOnlyList<PluginInfo> List(bool attentionOnly = true)
    {
        var root = Root;
        if (!Directory.Exists(root))
            return [];

        var groupState = LoadGroupState();
        var list = new List<PluginInfo>();
        foreach (var manifest in Directory.EnumerateFiles(root, ManifestFileName, SearchOption.AllDirectories))
        {
            if (!TryRead(manifest, groupState, out var info))
                continue;
            if (attentionOnly && !info.Attention)
                continue;
            list.Add(info);
        }

        return list
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(p => p.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<GroupInfo> ListGroups()
    {
        EnsureGroupsForInstalled();
        var state = LoadGroupState();
        var plugins = List(attentionOnly: false);
        var ids = new HashSet<string>(state.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var p in plugins)
        {
            foreach (var g in p.Groups)
                ids.Add(g);
        }

        return ids
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id =>
            {
                state.TryGetValue(id, out var st);
                var enabled = st?.Enabled ?? true;
                var label = st?.Label ?? PrettyLabel(id);
                var members = plugins.Count(p => p.Groups.Contains(id, StringComparer.OrdinalIgnoreCase));
                var attn = plugins.Count(p => p.Attention && p.Groups.Contains(id, StringComparer.OrdinalIgnoreCase));
                return new GroupInfo(id, label, enabled, members, attn);
            })
            .ToArray();
    }

    public static PluginInfo? Find(string idOrName, bool attentionOnly = false)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
            return null;
        var key = idOrName.Trim();
        return List(attentionOnly).FirstOrDefault(p =>
            string.Equals(p.Id, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.DisplayName, key, StringComparison.OrdinalIgnoreCase)
            || p.Id.EndsWith("." + key, StringComparison.OrdinalIgnoreCase)
            || p.Id.Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ResolvePlantUmlJar()
    {
        foreach (var p in List(attentionOnly: true))
        {
            if (!string.Equals(p.Mode, "A", StringComparison.OrdinalIgnoreCase))
                continue;
            if (p.JarPath is { Length: > 0 } && File.Exists(p.JarPath)
                && (p.Id.Contains("plantuml", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(p.JarPath).Contains("plantuml", StringComparison.OrdinalIgnoreCase)))
                return p.JarPath;
        }

        return null;
    }

    public static MutateResult SetPluginEnabled(string idOrRow, bool enabled)
    {
        var plugin = ResolvePlugin(idOrRow);
        if (plugin is null)
            return new MutateResult(false, "plugin_not_found", "id= or row=gN", null, null);

        if (!TryPatchManifest(plugin.ManifestPath, node =>
            {
                node["enabled"] = enabled;
            }))
            return new MutateResult(false, "manifest_write_failed", null, null, null);

        var refreshed = Find(plugin.Id);
        return new MutateResult(
            true,
            null,
            enabled ? "plugin on — in attention if groups on" : "plugin off — hidden from attention",
            refreshed,
            null);
    }

    public static MutateResult SetGroupEnabled(string groupId, bool enabled)
    {
        var id = NormalizeGroupId(groupId);
        if (id.Length == 0)
            return new MutateResult(false, "group_required", "group=javascript", null, null);

        var state = LoadGroupState();
        if (!state.TryGetValue(id, out var st))
            st = new GroupState(PrettyLabel(id), true);
        state[id] = st with { Enabled = enabled };
        SaveGroupState(state);

        var info = ListGroups().FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        return new MutateResult(
            true,
            null,
            enabled
                ? "group on — members return to attention (if plugin enabled)"
                : "group off — all members drop from attention",
            null,
            info);
    }

    public static MutateResult AddToGroup(string idOrRow, string groupId)
    {
        var plugin = ResolvePlugin(idOrRow);
        if (plugin is null)
            return new MutateResult(false, "plugin_not_found", "id=publisher.name", null, null);

        var gid = NormalizeGroupId(groupId);
        if (gid.Length == 0)
            return new MutateResult(false, "group_required", null, null, null);

        EnsureGroupRegistered(gid, PrettyLabel(gid));
        if (!TryPatchManifest(plugin.ManifestPath, node =>
            {
                var groups = ReadGroupsNode(node);
                if (!groups.Contains(gid, StringComparer.OrdinalIgnoreCase))
                    groups.Add(gid);
                WriteGroupsNode(node, groups);
                var manual = ReadStringArray(node, "groups_manual");
                if (!manual.Contains(gid, StringComparer.OrdinalIgnoreCase))
                    manual.Add(gid);
                node["groups_manual"] = JsonSerializer.SerializeToNode(manual);
            }))
            return new MutateResult(false, "manifest_write_failed", null, null, null);

        return new MutateResult(true, null, "added to group " + gid, Find(plugin.Id), null);
    }

    public static MutateResult RemoveFromGroup(string idOrRow, string groupId)
    {
        var plugin = ResolvePlugin(idOrRow);
        if (plugin is null)
            return new MutateResult(false, "plugin_not_found", null, null, null);

        var gid = NormalizeGroupId(groupId);
        if (gid.Length == 0)
            return new MutateResult(false, "group_required", null, null, null);

        if (!TryPatchManifest(plugin.ManifestPath, node =>
            {
                var groups = ReadGroupsNode(node);
                groups.RemoveAll(g => string.Equals(g, gid, StringComparison.OrdinalIgnoreCase));
                if (groups.Count == 0)
                    groups.Add("ungrouped");
                WriteGroupsNode(node, groups);
                var manual = ReadStringArray(node, "groups_manual");
                manual.RemoveAll(g => string.Equals(g, gid, StringComparison.OrdinalIgnoreCase));
                node["groups_manual"] = JsonSerializer.SerializeToNode(manual);
            }))
            return new MutateResult(false, "manifest_write_failed", null, null, null);

        return new MutateResult(true, null, "removed from group " + gid, Find(plugin.Id), null);
    }

    public static InstallResult InstallFromVsix(string vsixPath)
    {
        if (string.IsNullOrWhiteSpace(vsixPath) || !File.Exists(vsixPath))
            return new InstallResult(false, "vsix_not_found", null, "path= to .vsix");

        var work = Path.Combine(Path.GetTempPath(), "cdp-vsix-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var zipCopy = Path.Combine(work, "plugin.zip");
            File.Copy(vsixPath, zipCopy, overwrite: true);
            var unpack = Path.Combine(work, "unpack");
            ZipFile.ExtractToDirectory(zipCopy, unpack);

            var extDir = Path.Combine(unpack, "extension");
            if (!Directory.Exists(extDir))
            {
                if (File.Exists(Path.Combine(unpack, "package.json")))
                    extDir = unpack;
                else
                    return new InstallResult(false, "extension_folder_missing", null, "VSIX has no extension/");
            }

            return InstallFromUnpacked(extDir, vsixSource: Path.GetFullPath(vsixPath));
        }
        catch (Exception ex)
        {
            return new InstallResult(false, "unpack_failed", null, Trunc(ex.Message, 240));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* ignore */ }
        }
    }

    public static InstallResult InstallFromUnpacked(string extensionDir, string? vsixSource = null)
    {
        if (string.IsNullOrWhiteSpace(extensionDir) || !Directory.Exists(extensionDir))
            return new InstallResult(false, "extension_dir_missing", null, null);

        var pkgPath = Path.Combine(extensionDir, "package.json");
        if (!File.Exists(pkgPath))
            return new InstallResult(false, "package_json_missing", null, null);

        string name;
        string version;
        string publisher;
        string displayName;
        JsonElement pkgRoot;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkgPath));
            pkgRoot = doc.RootElement.Clone();
            name = Prop(pkgRoot, "name") ?? "plugin";
            version = Prop(pkgRoot, "version") ?? "0.0.0";
            publisher = Prop(pkgRoot, "publisher") ?? "unknown";
            displayName = Prop(pkgRoot, "displayName") ?? name;
        }
        catch (Exception ex)
        {
            return new InstallResult(false, "package_json_bad", null, Trunc(ex.Message, 200));
        }

        var id = $"{publisher}.{name}";
        var dest = Path.Combine(Root, id, version);
        Directory.CreateDirectory(dest);

        var destExt = Path.Combine(dest, "extension");
        if (Directory.Exists(destExt))
            Directory.Delete(destExt, recursive: true);
        CopyDirectory(extensionDir, destExt);

        var clasp = ClassifyExtension(dest, destExt, pkgRoot, displayName, id);
        var payload = clasp.Payload;
        var mode = clasp.Mode;
        var takeable = clasp.Takeable;

        var autoGroups = InferAutoGroups(pkgRoot, payload is not null, id, displayName);
        foreach (var g in autoGroups)
            EnsureGroupRegistered(g, PrettyLabel(g));

        var host = ProbeHostDeps(payload);
        var runtime = BuildRuntimeNode(payload, host);

        var manifest = new
        {
            schema = SchemaVersion,
            id = "openvsx:" + id,
            display_name = displayName,
            version,
            feature = clasp.Feature,
            verbs = clasp.Verbs,
            mode,
            enabled = takeable,
            groups = autoGroups,
            groups_auto = autoGroups,
            groups_manual = Array.Empty<string>(),
            source = new
            {
                vsix = vsixSource,
                publisher,
                name
            },
            runtime,
            delivery = clasp.Delivery,
            harvest = clasp.HarvestNode,
            installed_utc = DateTime.UtcNow.ToString("O")
        };

        var manifestPath = Path.Combine(dest, ManifestFileName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        var groupState = LoadGroupState();
        if (!TryRead(manifestPath, groupState, out var info))
        {
            info = new PluginInfo(
                "openvsx:" + id,
                displayName,
                version,
                mode,
                dest,
                payload?.AbsPath,
                payload?.Kind,
                manifestPath,
                Enabled: takeable,
                autoGroups,
                Attention: takeable);
        }

        var hostHint = FormatHostHint(host);
        return new InstallResult(
            true,
            null,
            info,
            clasp.Hint + hostHint + "; groups: " + string.Join(",", autoGroups));
    }

    // ── groups / attention ───────────────────────────────────────────

    sealed record GroupState(string Label, bool Enabled);

    static Dictionary<string, GroupState> LoadGroupState()
    {
        var path = GroupsPath;
        var map = new Dictionary<string, GroupState>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return map;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Object)
                return map;

            foreach (var p in groups.EnumerateObject())
            {
                var label = Prop(p.Value, "label") ?? PrettyLabel(p.Name);
                var enabled = !p.Value.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
                map[NormalizeGroupId(p.Name)] = new GroupState(label, enabled);
            }
        }
        catch
        {
            /* ignore corrupt */
        }

        return map;
    }

    static void SaveGroupState(Dictionary<string, GroupState> state)
    {
        Directory.CreateDirectory(Root);
        var groups = new JsonObject();
        foreach (var kv in state.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            groups[kv.Key] = new JsonObject
            {
                ["label"] = kv.Value.Label,
                ["enabled"] = kv.Value.Enabled
            };
        }

        var root = new JsonObject
        {
            ["schema"] = GroupsSchema,
            ["groups"] = groups
        };
        File.WriteAllText(GroupsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static void EnsureGroupRegistered(string id, string label)
    {
        var state = LoadGroupState();
        var key = NormalizeGroupId(id);
        if (key.Length == 0)
            return;
        if (!state.ContainsKey(key))
        {
            state[key] = new GroupState(label, true);
            SaveGroupState(state);
        }
    }

    static void EnsureGroupsForInstalled()
    {
        foreach (var p in List(attentionOnly: false))
        {
            foreach (var g in p.Groups)
                EnsureGroupRegistered(g, PrettyLabel(g));
        }
    }

    static bool IsAttention(bool pluginEnabled, IReadOnlyList<string> groups, Dictionary<string, GroupState> groupState)
    {
        if (!pluginEnabled)
            return false;
        if (groups.Count == 0)
            return true;
        foreach (var g in groups)
        {
            if (groupState.TryGetValue(g, out var st) && !st.Enabled)
                return false;
        }

        return true;
    }

    static List<string> InferAutoGroups(JsonElement pkg, bool hasPayload, string id, string displayName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? raw)
        {
            var g = NormalizeGroupId(raw);
            if (g.Length > 0)
                set.Add(g);
        }

        if (pkg.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cats.EnumerateArray())
                Add(c.GetString());
        }

        if (pkg.TryGetProperty("keywords", out var keys) && keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in keys.EnumerateArray())
            {
                var s = k.GetString();
                if (s is null) continue;
                // Only short topical keywords — skip sentences.
                if (s.Length <= 24 && !s.Contains(' '))
                    Add(s);
            }
        }

        if (pkg.TryGetProperty("contributes", out var contrib) && contrib.ValueKind == JsonValueKind.Object
            && contrib.TryGetProperty("languages", out var langs) && langs.ValueKind == JsonValueKind.Array)
        {
            foreach (var lang in langs.EnumerateArray())
            {
                var lid = Prop(lang, "id");
                if (lid is { Length: > 0 })
                    Add("lang-" + lid);
            }
        }

        var blob = (id + " " + displayName).ToLowerInvariant();
        if (blob.Contains("plantuml") || blob.Contains("uml") || hasPayload && blob.Contains("plant"))
            Add("diagrams");
        if (blob.Contains("javascript") || blob.Contains("typescript") || set.Contains("javascript") || set.Contains("typescript"))
            Add("javascript");
        if (blob.Contains("python"))
            Add("python");
        if (blob.Contains("markdown") || set.Contains("markdown"))
            Add("markdown");

        // Map common VS Code categories to short group ids
        if (set.Contains("programming-languages"))
        {
            set.Remove("programming-languages");
            // keep lang-* if any
        }

        if (set.Count == 0)
            set.Add("ungrouped");

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static PluginInfo? ResolvePlugin(string idOrRow)
    {
        var key = idOrRow.Trim();
        var all = List(attentionOnly: false);
        if (int.TryParse(key, out var n) || (key.StartsWith('g') && int.TryParse(key.AsSpan(1), out n)))
        {
            // Row indices follow attention list first, then fall back to all.
            var attn = List(attentionOnly: true);
            if (n >= 1 && n <= attn.Count)
                return attn[n - 1];
            if (n >= 1 && n <= all.Count)
                return all[n - 1];
        }

        return Find(key);
    }

    static bool TryPatchManifest(string path, Action<JsonObject> patch)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is null)
                return false;
            patch(node);
            File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    static List<string> ReadGroupsNode(JsonObject node)
    {
        var list = ReadStringArray(node, "groups");
        if (list.Count == 0)
            list.Add("ungrouped");
        return list;
    }

    static void WriteGroupsNode(JsonObject node, List<string> groups) =>
        node["groups"] = JsonSerializer.SerializeToNode(groups.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    static List<string> ReadStringArray(JsonObject node, string name)
    {
        var list = new List<string>();
        if (node[name] is not JsonArray arr)
            return list;
        foreach (var el in arr)
        {
            var s = el?.GetValue<string>();
            if (s is { Length: > 0 })
                list.Add(s);
        }

        return list;
    }

    static bool TryRead(string manifestPath, Dictionary<string, GroupState> groupState, out PluginInfo info)
    {
        info = default!;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            var id = Prop(root, "id") ?? Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(manifestPath)) ?? "plugin");
            var display = Prop(root, "display_name") ?? Prop(root, "displayName") ?? id;
            var version = Prop(root, "version") ?? "?";
            var mode = Prop(root, "mode") ?? "?";
            var enabled = !root.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
            var rootDir = Path.GetDirectoryName(manifestPath)!;
            string? payloadPath = null;
            string? payloadKind = null;
            if (root.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.Object)
            {
                payloadKind = Prop(rt, "kind");
                var rel = Prop(rt, "path") ?? Prop(rt, "jar") ?? Prop(rt, "exe");
                if (rel is { Length: > 0 })
                {
                    var abs = Path.Combine(rootDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(abs))
                        payloadPath = abs;
                }
            }

            if (payloadPath is null)
            {
                var extDir = Path.Combine(rootDir, "extension");
                if (Directory.Exists(extDir))
                {
                    var found = FindModeAPayload(rootDir, extDir);
                    if (found is not null)
                    {
                        payloadPath = found.AbsPath;
                        payloadKind = found.Kind;
                        mode = "A";
                    }
                }
            }
            else if (string.IsNullOrEmpty(payloadKind))
            {
                payloadKind = GuessKind(payloadPath);
            }

            if (payloadPath is not null && !string.Equals(mode, "A", StringComparison.OrdinalIgnoreCase))
                mode = "A";

            var groups = ReadStringArray(root, "groups");
            if (groups.Count == 0)
            {
                // Legacy manifest: try re-infer from extension/package.json
                var pkg = Path.Combine(rootDir, "extension", "package.json");
                if (File.Exists(pkg))
                {
                    try
                    {
                        using var pkgDoc = JsonDocument.Parse(File.ReadAllText(pkg));
                        groups = InferAutoGroups(pkgDoc.RootElement, hasPayload: payloadPath is not null, id, display);
                    }
                    catch
                    {
                        groups = ["ungrouped"];
                    }
                }
                else
                    groups = ["ungrouped"];
            }

            var attention = IsAttention(enabled, groups, groupState);
            info = new PluginInfo(id, display, version, mode, rootDir, payloadPath, payloadKind, manifestPath, enabled, groups, attention);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static List<string> ReadStringArray(JsonElement root, string name)
    {
        var list = new List<string>();
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (s is { Length: > 0 })
                    list.Add(s);
            }
        }

        return list;
    }

/// <summary>Rewrite mode/runtime/harvest from recursive tree + package.json. Mode D → enabled off.</summary>
    public static int ReharvestInstalled()
    {
        if (!Directory.Exists(Root))
            return 0;

        var n = 0;
        foreach (var manifestPath in Directory.EnumerateFiles(Root, ManifestFileName, SearchOption.AllDirectories))
        {
            var rootDir = Path.GetDirectoryName(manifestPath)!;
            var extDir = Path.Combine(rootDir, "extension");
            var pkgPath = Path.Combine(extDir, "package.json");
            if (!Directory.Exists(extDir) || !File.Exists(pkgPath))
                continue;

            JsonElement pkg;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pkgPath));
                pkg = doc.RootElement.Clone();
            }
            catch
            {
                continue;
            }

            var display = Prop(pkg, "displayName") ?? Prop(pkg, "name") ?? "plugin";
            var id = $"{Prop(pkg, "publisher") ?? "unknown"}.{Prop(pkg, "name") ?? "plugin"}";
            var clasp = ClassifyExtension(rootDir, extDir, pkg, display, id);
            var host = ProbeHostDeps(clasp.Payload);
            if (!TryPatchManifest(manifestPath, node =>
                {
                    node["schema"] = SchemaVersion;
                    node["mode"] = clasp.Mode;
                    node["feature"] = clasp.Feature;
                    node["verbs"] = JsonSerializer.SerializeToNode(clasp.Verbs);
                    if (clasp.Payload is null)
                        node.Remove("runtime");
                    else
                        node["runtime"] = JsonSerializer.SerializeToNode(BuildRuntimeNode(clasp.Payload, host));
                    node["delivery"] = JsonSerializer.SerializeToNode(clasp.Delivery);
                    node["harvest"] = JsonSerializer.SerializeToNode(clasp.HarvestNode);
                    if (!clasp.Takeable)
                        node["enabled"] = false;
                }))
                continue;
            n++;
        }

        return n;
    }

    internal sealed record ModeAPayload(string Kind, string RelPath, string AbsPath);

    internal sealed record HostDep(string Name, bool Ok, string? ResolvedPath);

    sealed record HarvestClass(
        string Mode,
        ModeAPayload? Payload,
        bool Takeable,
        string Hint,
        string Feature,
        string[] Verbs,
        object Delivery,
        object HarvestNode);

    /// <summary>
    /// Discover delivery + triage: recursive assets / LSP / refuse → Mode A|B|D.
    /// </summary>
    static HarvestClass ClassifyExtension(
        string pluginRoot,
        string extensionDir,
        JsonElement pkg,
        string displayName,
        string id)
    {
        var contributesKeys = ListContributesKeys(pkg);
        var assets = FindAllModeAPayloads(pluginRoot, extensionDir);
        var payload = assets.Count > 0 ? assets[0] : null;
        var lsp = DetectLspSignals(pkg, extensionDir);
        var fetchHint = DetectFetchOnActivate(pkg, extensionDir, payload);
        var feature = InferFeature(id, displayName, pkg);

        string mode;
        bool takeable;
        string hint;
        var channels = new List<string>();

        if (payload is not null)
        {
            mode = "A";
            takeable = true;
            channels.Add("bundled_tool");
            hint = $"Mode A — takeable · {payload.Kind} {payload.RelPath}"
                   + (assets.Count > 1 ? $" (+{assets.Count - 1} more)" : "");
        }
        else if (lsp.Hit)
        {
            mode = "B";
            takeable = true;
            channels.Add("lsp");
            hint = "Mode B — takeable (LSP: " + lsp.Why + "); host wiring later";
        }
        else
        {
            mode = "D";
            takeable = false;
            channels.Add(fetchHint is { Length: > 0 } ? "fetch_on_activate" : "ui_only");
            hint = fetchHint is { Length: > 0 }
                ? "Mode D — refuse · " + fetchHint
                : "Mode D — refuse · no bundled tool/PATH asset and no LSP signal";
        }

        if (payload?.Kind is "jar")
            channels.Add("host_java");

        var verbs = InferVerbs(feature, mode, payload);
        var kept = new List<string>();
        if (payload is not null) kept.Add("assets");
        if (lsp.Hit) kept.Add("language_server");
        if (contributesKeys.Contains("languages", StringComparer.OrdinalIgnoreCase)) kept.Add("languages");
        if (contributesKeys.Contains("configuration", StringComparer.OrdinalIgnoreCase)) kept.Add("configuration");

        var refused = new[]
        {
            "menus", "keybindings", "main_extension_host", "webview_preview_ui"
        };

        var assetRows = assets.Take(24).Select(a => new { kind = a.Kind, path = a.RelPath }).ToArray();
        var delivery = new
        {
            channels = channels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            assets = assetRows,
            primary = payload is null ? null : new { kind = payload.Kind, path = payload.RelPath },
            lsp = lsp.Hit ? lsp.Why : null,
            fetch = fetchHint,
            why = hint
        };

        var harvestNode = new
        {
            mode,
            takeable,
            feature,
            verbs,
            why = hint,
            lsp = lsp.Hit ? lsp.Why : null,
            fetch = fetchHint,
            contributes_keys = contributesKeys,
            contributes_kept = kept.ToArray(),
            contributes_refused = refused,
            assets = assetRows,
            primary = payload is null ? null : new { kind = payload.Kind, path = payload.RelPath }
        };

        return new HarvestClass(mode, payload, takeable, hint, feature, verbs, delivery, harvestNode);
    }

    static string InferFeature(string id, string displayName, JsonElement pkg)
    {
        var blob = (id + " " + displayName).ToLowerInvariant();
        if (blob.Contains("plantuml")) return "plantuml";
        if (blob.Contains("shellcheck")) return "shellcheck";
        if (blob.Contains("checkstyle") || blob.Contains("java-code-checker") || blob.Contains("spotbugs") || blob.Contains("pmd"))
            return "java-lint";
        if (blob.Contains("graphviz") || blob.Contains("dot")) return "graphviz";
        if (blob.Contains("mermaid")) return "mermaid";
        if (pkg.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            return n.GetString() ?? "feature";
        return "feature";
    }

    static string[] InferVerbs(string feature, string mode, ModeAPayload? payload)
    {
        if (string.Equals(mode, "D", StringComparison.OrdinalIgnoreCase))
            return [];
        if (feature is "plantuml" || payload?.RelPath.Contains("plantuml", StringComparison.OrdinalIgnoreCase) == true)
            return ["preview"];
        if (feature is "shellcheck" or "java-lint")
            return ["lint"];
        if (string.Equals(mode, "B", StringComparison.OrdinalIgnoreCase))
            return ["diags"];
        return ["run"];
    }

    static string? DetectFetchOnActivate(JsonElement pkg, string extensionDir, ModeAPayload? payload)
    {
        if (payload is not null)
            return null;
        var blob = (Prop(pkg, "name") ?? "") + " " + (Prop(pkg, "displayName") ?? "");
        if (blob.Contains("shellcheck", StringComparison.OrdinalIgnoreCase))
            return "downloads shellcheck binary on VS Code activate — not in VSIX";

        try
        {
            var raw = pkg.GetRawText();
            if (raw.Contains("downloadRelease", StringComparison.OrdinalIgnoreCase)
                || raw.Contains("github.com/koalaman/shellcheck", StringComparison.OrdinalIgnoreCase))
                return "extension fetches tool at activate — not bundled";
        }
        catch { /* ignore */ }

        return null;
    }

    /// <summary>Probe PATH for host tools required by primary payload (e.g. java for jar).</summary>
    internal static IReadOnlyList<HostDep> ProbeHostDeps(ModeAPayload? payload)
    {
        if (payload is null)
            return [];
        if (string.Equals(payload.Kind, "jar", StringComparison.OrdinalIgnoreCase))
            return [ProbeOnPath("java")];
        return [];
    }

    internal static object ProbeHostDepsCard(ModeAPayload? payload)
    {
        var deps = ProbeHostDeps(payload);
        return new
        {
            host_deps = deps.Select(d => new { name = d.Name, ok = d.Ok, path = d.ResolvedPath }).ToArray(),
            host_ok = deps.Count == 0 || deps.All(d => d.Ok)
        };
    }

    public static object HostProbeCard(PluginInfo? plugin)
    {
        if (plugin?.PayloadPath is not { Length: > 0 })
            return ProbeHostDepsCard(null);
        var kind = plugin.PayloadKind ?? GuessKind(plugin.PayloadPath);
        return ProbeHostDepsCard(new ModeAPayload(kind, plugin.PayloadPath, plugin.PayloadPath));
    }


    static HostDep ProbeOnPath(string command)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where.exe" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
                return new HostDep(command, false, null);
            var stdout = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new HostDep(command, false, null);
            }

            var first = stdout
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return new HostDep(command, first is { Length: > 0 }, first);
        }
        catch
        {
            return new HostDep(command, false, null);
        }
    }

    static string FormatHostHint(IReadOnlyList<HostDep> host)
    {
        if (host.Count == 0)
            return "";
        var missing = host.Where(h => !h.Ok).Select(h => h.Name).ToArray();
        if (missing.Length == 0)
            return " · host ok (" + string.Join(",", host.Select(h => h.Name)) + ")";
        return " · host missing: " + string.Join(",", missing);
    }

    static string[] ListContributesKeys(JsonElement pkg)
    {
        if (!pkg.TryGetProperty("contributes", out var c) || c.ValueKind != JsonValueKind.Object)
            return [];
        return c.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static (bool Hit, string Why) DetectLspSignals(JsonElement pkg, string extensionDir)
    {
        try
        {
            var raw = pkg.GetRawText();
            if (raw.Contains("vscode-languageclient", StringComparison.OrdinalIgnoreCase)
                || raw.Contains("LanguageClient", StringComparison.Ordinal))
                return (true, "package.json LanguageClient");
        }
        catch { /* ignore */ }

        foreach (var file in EnumeratePayloadCandidateFiles(extensionDir))
        {
            var name = Path.GetFileName(file);
            if (name.Contains("language-server", StringComparison.OrdinalIgnoreCase)
                || name.Contains("languageserver", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".lsp", StringComparison.OrdinalIgnoreCase)
                || name.Equals("server.js", StringComparison.OrdinalIgnoreCase)
                   && (Path.GetFileName(Path.GetDirectoryName(file) ?? "").Equals("server", StringComparison.OrdinalIgnoreCase)
                       || Path.GetFileName(Path.GetDirectoryName(file) ?? "").Equals("lsp", StringComparison.OrdinalIgnoreCase)))
                return (true, "tree:" + name);
        }

        return (false, "");
    }

    static object? BuildRuntimeNode(ModeAPayload? payload, IReadOnlyList<HostDep>? host = null)
    {
        if (payload is null)
            return null;

        var path = payload.RelPath.Replace('\\', '/');
        var hostDeps = (host ?? ProbeHostDeps(payload))
            .Select(d => new { name = d.Name, ok = d.Ok, path = d.ResolvedPath })
            .ToArray();

        return payload.Kind switch
        {
            "jar" => new
            {
                kind = "jar",
                exe = "java",
                jar = path,
                path,
                formats = new[] { "png", "svg" },
                host_deps = hostDeps
            },
            "exe" => new { kind = "exe", path, exe = path, host_deps = hostDeps },
            "wasm" => new { kind = "wasm", path, host_deps = hostDeps },
            _ => new { kind = payload.Kind, path, exe = path, host_deps = hostDeps }
        };
    }

    static ModeAPayload? FindModeAPayload(string pluginRoot, string extensionDir)
    {
        var all = FindAllModeAPayloads(pluginRoot, extensionDir);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>Recursive scan; best tool payload first (not dependency jars).</summary>
    static List<ModeAPayload> FindAllModeAPayloads(string pluginRoot, string extensionDir)
    {
        var scored = new List<(int Score, ModeAPayload Payload)>();
        if (!Directory.Exists(extensionDir))
            return [];

        foreach (var file in EnumeratePayloadCandidateFiles(extensionDir))
        {
            if (!TryScorePayload(file, out var kind, out var score))
                continue;
            var rel = Path.GetRelativePath(pluginRoot, file).Replace('\\', '/');
            scored.Add((score, new ModeAPayload(kind, rel, file)));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Payload.RelPath.Length)
            .Select(x => x.Payload)
            .ToList();
    }

    static IEnumerable<string> EnumeratePayloadCandidateFiles(string extensionDir)
    {
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "out", "test", "tests", "__tests__", "fixtures", ".vs", "obj"
        };

        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((extensionDir, 0));
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            if (depth > 10)
                continue;

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var f in files)
                yield return f;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (skipDirs.Contains(name))
                    continue;
                stack.Push((sub, depth + 1));
            }
        }
    }

    static bool TryScorePayload(string file, out string kind, out int score)
    {
        kind = "";
        score = 0;
        var ext = Path.GetExtension(file);
        var name = Path.GetFileName(file);
        var parent = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
        var inBinish = parent is "bin" or "tools" or "native" or "binaries" or "runtimes";
        var underLib = file.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                       || parent.Equals("lib", StringComparison.OrdinalIgnoreCase);

        if (ext.Equals(".jar", StringComparison.OrdinalIgnoreCase))
        {
            if (IsDependencyJarName(name))
                return false;

            kind = "jar";
            var n = name.ToLowerInvariant();
            if (n.Contains("plantuml")) score = 120;
            else if (n.EndsWith("-all.jar") || n.Contains("-all-")) score = 115;
            else if (n.Contains("cli")) score = 112;
            else if (n is "spotbugs.jar" or "checkstyle.jar" or "pmd.jar") score = 110;
            else if (n.StartsWith("checkstyle") || n.StartsWith("pmd-") || n.StartsWith("spotbugs"))
                score = underLib ? 100 : 108;
            else if (n.Contains("plugin"))
                score = 45;
            else
                score = underLib ? 70 : 95;
            return true;
        }

        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            kind = "exe";
            score = inBinish ? 95 : 90;
            return true;
        }

        if (ext.Equals(".wasm", StringComparison.OrdinalIgnoreCase))
        {
            kind = "wasm";
            score = 80;
            return true;
        }

        if (inBinish)
        {
            if (ext.Length == 0 || ext.Equals(".bin", StringComparison.OrdinalIgnoreCase))
            {
                kind = "bin";
                score = 70;
                return true;
            }

            if (ext is ".cmd" or ".bat" or ".ps1" or ".sh")
            {
                kind = "bin";
                score = 55;
                return true;
            }
        }

        return false;
    }

    static bool IsDependencyJarName(string name)
    {
        var n = name.ToLowerInvariant();
        ReadOnlySpan<string> prefixes =
        [
            "asm-", "asm_", "bcel-", "commons-", "guava", "gson-", "slf4j", "log4j",
            "httpclient", "httpcore", "kotlin-", "scala-", "rhino-", "antlr", "picocli-",
            "jaxen-", "dom4j-", "jcip-", "jsr305", "jsr250", "error_prone", "checker-",
            "j2objc", "listenablefuture", "failureaccess", "jspecify", "pcollections",
            "xmlresolver", "saxon-", "progressbar", "jline-", "jna-", "jsoup-",
            "flogger", "better-files", "directory-watcher", "geny_", "sourcecode_",
            "ujson_", "upack_", "upickle", "trees_", "parsers_", "io_", "common_",
            "scalajs-", "groovy-", "spotbugs-annotations", "spotbugs-ant"
        ];
        foreach (var p in prefixes)
        {
            if (n.StartsWith(p, StringComparison.Ordinal))
                return true;
        }

        return n.EndsWith("-annotations.jar", StringComparison.Ordinal)
               || n.Contains("annotation", StringComparison.Ordinal);
    }

    static string GuessKind(string absPath)
    {
        var ext = Path.GetExtension(absPath);
        if (ext.Equals(".jar", StringComparison.OrdinalIgnoreCase)) return "jar";
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return "exe";
        if (ext.Equals(".wasm", StringComparison.OrdinalIgnoreCase)) return "wasm";
        return "bin";
    }

    static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(dir);
            if (name is "node_modules" or ".git")
                continue;
            CopyDirectory(dir, Path.Combine(dest, name));
        }
    }

    static string NormalizeGroupId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var s = raw.Trim().ToLowerInvariant();
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        var prevDash = false;
        foreach (var ch in s)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                buf[n++] = ch;
                prevDash = false;
            }
            else if (!prevDash)
            {
                buf[n++] = '-';
                prevDash = true;
            }
        }

        var id = new string(buf[..n]).Trim('-');
        return id.Length > 32 ? id[..32].TrimEnd('-') : id;
    }

    static string PrettyLabel(string id)
    {
        if (id.StartsWith("lang-", StringComparison.OrdinalIgnoreCase))
            return "Lang: " + id[5..];
        if (id is "ungrouped")
            return "Ungrouped";
        if (id.Length == 0)
            return id;
        return char.ToUpperInvariant(id[0]) + id[1..].Replace('-', ' ');
    }

    static string? Prop(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
