#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent plugins|plugin|vsix — IdePluginsChannel (go=plugins; no steal bare list/search/enable/install).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RoutePlugins(string raw)
    {
        var work = NormalizePluginsCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("plugins ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("plugin ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("vsix ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "list" : op.Trim().ToLowerInvariant();
        op = NormalizePluginsOp(op);

        if (!IsPluginsOp(op))
            return new Route(Verb.Plugins, raw, Ok: false, Reason: "plugins_op_unknown");

        var path = ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "plugin")
            ?? ExtractKeyedValue(work, "name")
            ?? ExtractKeyedValue(work, "q")
            ?? ExtractKeyedValue(work, "query");

        return new Route(
            Verb.Plugins,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "plugins");
    }

    static string NormalizePluginsCompound(string raw)
    {
        foreach (var (prefix, op) in PluginsCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "plugins " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "plugins" + rest;
            return "plugins " + op + rest;
        }

        foreach (var alias in PluginsAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "plugins";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "plugins " + raw[alias.Length..].TrimStart();
        }

        if (raw.Equals("plugins", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("plugins ", StringComparison.OrdinalIgnoreCase))
            return raw;

        return raw;
    }

    static readonly (string Prefix, string Op)[] PluginsCompounds =
    [
        ("plugins_list", "list"),
        ("plugins_search", "search"),
        ("plugins_groups", "groups"),
        ("plugins_group", "group"),
        ("plugins_enable", "enable"),
        ("plugins_disable", "disable"),
        ("plugins_install", "install"),
        ("plugins_want", "want"),
        ("plugins_reharvest", "reharvest"),
        ("plugins_preview", "preview"),
        ("plugin_list", "list"),
        ("plugin_search", "search"),
        ("plugin_enable", "enable"),
        ("plugin_install", "install"),
        ("vsix_search", "search"),
        ("vsix_install", "install")
    ];

    static readonly string[] PluginsAliases =
    [
        "plugin",
        "vsix"
    ];

    static string NormalizePluginsOp(string op) =>
        op switch
        {
            "installed" => "list",
            "find" or "query" => "search",
            "grouplist" => "groups",
            "tag" => "group",
            "on" => "enable",
            "off" => "disable",
            "add" => "install",
            "need" or "get" => "want",
            "rescan" or "reclassify" => "reharvest",
            "render" or "png" => "preview",
            _ => op
        };

    static bool IsPluginsOp(string? op) =>
        op is "list" or "search" or "groups" or "group" or "enable" or "disable"
            or "install" or "want" or "reharvest" or "preview";

    static bool IsPluginsIntent(string raw)
    {
        if (raw.Equals("plugins", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("plugins ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in PluginsAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in PluginsCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
