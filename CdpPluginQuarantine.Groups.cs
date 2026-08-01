#nullable enable
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

internal static partial class CdpPluginQuarantine
{
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

}
