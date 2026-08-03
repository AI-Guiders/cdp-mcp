#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent settings|options — cdp_settings without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteSettings(string raw)
    {
        var work = NormalizeSettingsCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("settings ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("options ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("prefs ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("ide_settings ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("tools_options ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("languages ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        if (work.Equals("languages", StringComparison.OrdinalIgnoreCase)
            || work.Equals("languages_page", StringComparison.OrdinalIgnoreCase))
            op = string.IsNullOrWhiteSpace(op) ? "page" : op;

        op = string.IsNullOrWhiteSpace(op) ? "options" : op.Trim().ToLowerInvariant();
        op = NormalizeSettingsOp(op);

        if (!IsSettingsOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "settings_op_unknown");

        var page = ExtractKeyedValue(work, "page")
            ?? ExtractKeyedValue(work, "section")
            ?? ExtractKeyedValue(work, "category");
        if (string.IsNullOrWhiteSpace(page)
            && (work.Equals("languages", StringComparison.OrdinalIgnoreCase)
                || work.Equals("languages_page", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("languages ", StringComparison.OrdinalIgnoreCase)))
            page = "languages";
        if (string.IsNullOrWhiteSpace(page) && op is "page")
            page = TryPositionalAfterOp(work, "page");

        var key = ExtractKeyedValue(work, "key")
            ?? ExtractKeyedValue(work, "name");
        if (string.IsNullOrWhiteSpace(key) && (op is "get" or "set" or "unset"))
            key = TryPositionalAfterOp(work, op);

        var value = ExtractKeyedValue(work, "value")
            ?? ExtractKeyedValue(work, "val")
            ?? ExtractKeyedValue(work, "to");
        var id = ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "language");
        if (string.IsNullOrWhiteSpace(id) && op.StartsWith("lsp_", StringComparison.Ordinal))
            id = TryPositionalAfterOp(work, op);

        var via = ExtractKeyedValue(work, "via");
        var command = ExtractKeyedValue(work, "command")
            ?? ExtractKeyedValue(work, "exe");

        if (op is "page" && string.IsNullOrWhiteSpace(page))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "settings_page_required");
        if ((op is "get" or "unset") && string.IsNullOrWhiteSpace(key))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "settings_key_required");
        if (op is "set" && (string.IsNullOrWhiteSpace(key) || value is null))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "settings_key_value_required");
        if ((op is "lsp_install" or "lsp_ensure" or "lsp_add") && string.IsNullOrWhiteSpace(id))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "settings_lsp_id_required");
        if (op is "lsp_add" && string.IsNullOrWhiteSpace(command))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "settings_lsp_command_required");

        var path = op switch
        {
            "page" => page,
            "get" or "set" or "unset" => key,
            _ => page ?? key
        };

        return new Route(
            Verb.Settings,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Tool: op is "set" ? value : id,
            Detail: via,
            Command: command,
            Scene: page,
            Go: "settings");
    }

    static string NormalizeSettingsCompound(string raw)
    {
        foreach (var (prefix, op) in SettingsCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "settings " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "settings" + rest;
            return "settings " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] SettingsCompounds =
    [
        ("settings_scene", "options"),
        ("settings_page", "page"),
        ("settings_catalog", "catalog"),
        ("settings_get", "get"),
        ("settings_set", "set"),
        ("settings_unset", "unset"),
        ("settings_which", "which"),
        ("options_page", "page"),
        ("tools_options", "options"),
        ("ide_settings", "options"),
        ("languages_page", "page"),
        ("lsp_probe", "lsp_probe"),
        ("lsp_status", "lsp_probe"),
        ("lsp_install", "lsp_install"),
        ("lsp_ensure", "lsp_ensure"),
        ("lsp_add", "lsp_add")
    ];

    static string NormalizeSettingsOp(string op) =>
        op switch
        {
            "scene" or "status" or "tools" or "prefs" or "map" => "options",
            "category" or "open" => "page",
            "keys" or "list" => "catalog",
            "reset" or "clear" => "unset",
            "path" => "which",
            "lsp_status" => "lsp_probe",
            _ => op
        };

    static bool IsSettingsOp(string? op) =>
        op is "options" or "page" or "catalog" or "get" or "set" or "unset"
            or "which" or "lsp_probe" or "lsp_install" or "lsp_ensure" or "lsp_add";
}
