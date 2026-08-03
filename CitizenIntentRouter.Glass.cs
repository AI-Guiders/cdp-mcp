#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent glass|surface_desk|cdp_glass — IdeGlassSurfaceChannel (go=surface_desk).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteGlass(string raw)
    {
        var work = NormalizeGlassCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("glass ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_glass ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("surface_desk ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizeGlassOp(op);

        if (!IsGlassOp(op))
            return new Route(Verb.Glass, raw, Ok: false, Reason: "glass_op_unknown");

        return new Route(
            Verb.Glass,
            raw,
            Ok: true,
            Op: op,
            Go: "surface_desk");
    }

    static string NormalizeGlassCompound(string raw)
    {
        foreach (var (prefix, op) in GlassCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "glass " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "glass" + rest;
            return "glass " + op + rest;
        }

        foreach (var alias in GlassAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "glass";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "glass " + raw[alias.Length..].TrimStart();
        }

        if (raw.StartsWith("glass_", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("glass_surface", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw["glass_".Length..];
            var sp = rest.IndexOf(' ');
            var head = sp < 0 ? rest : rest[..sp];
            if (IsGlassOp(NormalizeGlassOp(head)))
                return "glass " + rest;
        }

        if (raw.StartsWith("cdp_glass_", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw["cdp_glass_".Length..];
            var sp = rest.IndexOf(' ');
            var head = sp < 0 ? rest : rest[..sp];
            if (IsGlassOp(NormalizeGlassOp(head)))
                return "glass " + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] GlassCompounds =
    [
        ("glass_scene", "scene"),
        ("glass_status", "status"),
        ("glass_caps", "caps"),
        ("glass_layout", "layout"),
        ("glass_highlight", "highlight"),
        ("glass_focus", "focus"),
        ("glass_click", "click"),
        ("glass_palette", "palette"),
        ("glass_run", "run"),
        ("glass_action", "action"),
        ("glass_appearance", "appearance"),
        ("glass_colors", "colors"),
        ("surface_desk_scene", "scene"),
        ("surface_desk_status", "status"),
        ("surface_desk_layout", "layout"),
        ("surface_desk_focus", "focus"),
        ("cdp_glass_scene", "scene"),
        ("cdp_glass_status", "status"),
        ("cdp_glass_layout", "layout"),
        ("cdp_glass_focus", "focus"),
        ("cdp_glass_run", "run")
    ];

    static readonly string[] GlassAliases =
    [
        "cdp_glass",
        "surface_desk"
    ];

    static string NormalizeGlassOp(string op) =>
        op switch
        {
            "desk" or "help" or "a" or "map" => "scene",
            _ => op
        };

    static bool IsGlassOp(string? op) =>
        op is "scene" or "status" or "caps" or "layout" or "highlight" or "focus" or "click"
            or "set_text" or "send_keys" or "palette" or "run" or "action" or "appearance"
            or "colors" or "colors_under_cursor" or "set_control_layout" or "set_panel_size"
            or "request_confirmation";

    static bool IsGlassIntent(string raw)
    {
        if (raw.Equals("glass", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("glass ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in GlassAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in GlassCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (raw.StartsWith("glass_", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("glass_surface", StringComparison.OrdinalIgnoreCase))
            return true;

        if (raw.StartsWith("cdp_glass_", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
