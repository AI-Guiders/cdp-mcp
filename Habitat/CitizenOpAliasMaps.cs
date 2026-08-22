#nullable enable

namespace CdpMcp.Habitat;

/// <summary>Canonical op aliases for Citizen intent routers (data tables).</summary>
internal static class CitizenOpAliasMaps
{
    internal static readonly Dictionary<string, string> Mcp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["status"] = "scene",
        ["list"] = "scene",
        ["invoke"] = "call",
        ["list_tools"] = "tools",
        ["connect"] = "mount",
        ["add"] = "mount",
        ["catalog"] = "presets",
    };

    internal static readonly Dictionary<string, string> Debug = new(StringComparer.OrdinalIgnoreCase)
    {
        ["status"] = "scene",
        ["list"] = "bp_list",
        ["cont"] = "continue",
    };

    internal static readonly Dictionary<string, string> Clip = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cp"] = "copy",
        ["copy"] = "copy",
        ["cut"] = "cut",
        ["scissors"] = "cut",
        ["paste"] = "paste",
        ["p"] = "paste",
        ["clip"] = "clipboard",
        ["clipboard"] = "clipboard",
        ["clips"] = "clipboard",
        ["clipboard_clear"] = "clipboard_clear",
        ["clip_clear"] = "clipboard_clear",
        ["clear_clip"] = "clipboard_clear",
    };

    internal static readonly Dictionary<string, string> Nav = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "back",
        ["back"] = "back",
        ["prev"] = "back",
        ["f"] = "forward",
        ["fwd"] = "forward",
        ["forward"] = "forward",
        ["next"] = "forward",
        ["status"] = "nav",
        ["nav"] = "nav",
        ["nav_status"] = "nav",
        ["recent"] = "recent_files",
        ["recent_files"] = "recent_files",
        ["mru"] = "recent_files",
    };

    internal static readonly Dictionary<string, string> Disk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reload"] = "reload",
        ["from_disk"] = "reload",
        ["revert_disk"] = "reload",
        ["keep_disk"] = "keep_disk",
        ["keepdisk"] = "keep_disk",
        ["dont_reload"] = "keep_disk",
        ["don't_reload"] = "keep_disk",
        ["keep"] = "keep_disk",
        ["disk_peek"] = "disk_peek",
        ["diskpeek"] = "disk_peek",
        ["peek_disk"] = "disk_peek",
        ["peek"] = "disk_peek",
    };

    internal static readonly Dictionary<string, string> FindBuf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["find_all"] = "find_all",
        ["findall"] = "find_all",
        ["all"] = "find_all",
        ["find"] = "find",
        ["buf_find"] = "find",
        ["buffer_find"] = "find",
        ["find_in"] = "find",
        ["find_buffer"] = "find",
        ["search"] = "find",
    };

    internal static readonly Dictionary<string, string> Undo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["u"] = "undo",
        ["undo"] = "undo",
        ["revert"] = "undo",
        ["r"] = "redo",
        ["redo"] = "redo",
        ["unundo"] = "redo",
        ["h"] = "history",
        ["history"] = "history",
        ["stack"] = "history",
        ["edit_history"] = "history",
    };

    internal static readonly Dictionary<string, string> Buffer = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read"] = "read",
        ["doc_read"] = "read",
        ["buffer_read"] = "read",
        ["close"] = "close",
        ["doc_close"] = "close",
        ["buffer_close"] = "close",
        ["scene"] = "scene",
        ["buffers"] = "scene",
        ["list"] = "scene",
        ["doc_scene"] = "scene",
        ["buffer_scene"] = "scene",
        ["diagnostics"] = "diagnostics",
        ["diags"] = "diagnostics",
        ["diag"] = "diagnostics",
        ["doc_diagnostics"] = "diagnostics",
        ["buffer_diagnostics"] = "diagnostics",
        ["buf_diagnostics"] = "diagnostics",
        ["buf_diags"] = "diagnostics",
    };

    internal static readonly Dictionary<string, string> Sniper = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scope"] = "scope",
        ["set"] = "scope",
        ["lock"] = "scope",
        ["peek"] = "peek",
        ["view"] = "peek",
        ["target"] = "target",
        ["outline"] = "target",
        ["aim"] = "aim",
        ["clear"] = "clear",
        ["scope_clear"] = "clear",
        ["sniperclear"] = "clear",
        ["sniper_clear"] = "clear",
        ["status"] = "status",
        ["show"] = "status",
    };
}
