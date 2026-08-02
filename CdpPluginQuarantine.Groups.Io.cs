#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;
internal static partial class CdpPluginQuarantine
{
    static List<string> ReadGroupsNode(JsonObject node)
    {
        var list = ReadStringArray(node, "groups");
        if (list.Count == 0)
            list.Add("ungrouped");
        return list;
    }

    static void WriteGroupsNode(JsonObject node, List<string> groups) => node["groups"] = JsonSerializer.SerializeToNode(groups.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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