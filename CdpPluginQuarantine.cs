#nullable enable
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

/// <summary>
/// Mode-A plugin quarantine under %LocalAppData%/cdp-mcp/plugins/.
/// Groups (auto+manual) + enable/disable — attention filter for agent UX.
/// </summary>
internal static partial class CdpPluginQuarantine
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
}
