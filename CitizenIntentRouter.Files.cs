#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent files|files_desk|cdp_files — IdeFilesChannel without Cursor MCP (go=files_desk place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteFiles(string raw)
    {
        var work = NormalizeFilesCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("files ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("files_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_files ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("file_manager ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("fm ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeFilesOp(op);

        if (!IsFilesOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "files_op_unknown");

        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "to")
            ?? ExtractKeyedValue(work, "name")
            ?? ExtractKeyedValue(work, "file");

        return new Route(
            Verb.Files,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "files_desk");
    }

    static string NormalizeFilesCompound(string raw)
    {
        foreach (var (prefix, op) in FilesCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "files " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "files" + rest;
            return "files " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] FilesCompounds =
    [
        ("files_scene", "scene"),
        ("files_desk", "scene"),
        ("files_list", "list"),
        ("files_ls", "list"),
        ("files_cd", "cd"),
        ("files_up", "up"),
        ("files_stat", "stat"),
        ("files_tree", "tree"),
        ("files_open", "open"),
        ("files_text", "text"),
        ("files_read", "text"),
        ("files_search", "search"),
        ("files_roots", "roots"),
        ("files_clear", "clear"),
        ("cdp_files_scene", "scene"),
        ("cdp_files_list", "list"),
        ("cdp_files_cd", "cd"),
        ("cdp_files_up", "up"),
        ("cdp_files_stat", "stat"),
        ("cdp_files_tree", "tree"),
        ("cdp_files_open", "open"),
        ("cdp_files_text", "text"),
        ("cdp_files_search", "search"),
        ("cdp_files_roots", "roots"),
        ("cdp_files_clear", "clear"),
        ("fm_scene", "scene"),
        ("fm_list", "list"),
        ("fm_cd", "cd"),
        ("fm_tree", "tree"),
        ("fm_open", "open")
    ];

    static string NormalizeFilesOp(string op) =>
        op switch
        {
            "ls" or "dir" => "list",
            "chdir" => "cd",
            ".." or "cdup" => "up",
            "info" => "stat",
            "dump" or "read" => "text",
            "find" => "search",
            "desk" or "status" or "show" or "pulse" or "map" => "scene",
            _ => op
        };

    static bool IsFilesOp(string? op) =>
        op is "scene" or "list" or "cd" or "up" or "stat" or "tree"
            or "open" or "text" or "search" or "roots" or "clear";
}
