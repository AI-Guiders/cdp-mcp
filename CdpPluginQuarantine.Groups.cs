#nullable enable
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


}
