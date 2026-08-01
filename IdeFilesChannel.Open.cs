#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Open/search ops for IdeFilesChannel (buffer open + find_desk facet).</summary>
internal static partial class IdeFilesChannel
{
    static object OpenFile(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var target = Opt(args, "path") ?? Opt(args, "name");
        if (string.IsNullOrWhiteSpace(target))
            return Err("path_required", "open path=");

        var cwd = ResolveCwd(session, args, out _);
        string full;
        try
        {
            full = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(cwd, target));
        }
        catch (Exception ex)
        {
            return Err("bad_path", ex.Message);
        }

        if (Directory.Exists(full))
        {
            SetCwd(full);
            return Cd(session, Dict(("path", full)));
        }

        if (!File.Exists(full))
            return Err("not_found", full);

        var asMode = (Opt(args, "as") ?? Opt(args, "mode") ?? "").Trim().ToLowerInvariant();
        if (asMode is not ("buffer" or "edit") && IsTextProjectable(full))
            return TextProject(session, Dict(("path", full), ("max_chars", Opt(args, "max_chars") ?? "")));

        try
        {
            var buf = store.Open(full);
            return new
            {
                ok = true,
                schema = SchemaVersion,
                go = "files_desk",
                tool = ToolName,
                op = "open",
                pulse = $"files · open · {Path.GetFileName(full)}",
                path = full,
                doc_id = buf.DocId,
                next = new object[]
                {
                    new { go = "editor_scene", label = "Editor", why = "buffer open" },
                    new { go = "files_desk", label = "Text dump", why = $"op=text path={full}" },
                    new { go = "files_desk", label = "Cwd list", why = "op=list" }
                },
                hint = "Opened into cdp_buffer — edit via buffer plane; op=text for lynx-like dump"
            };
        }
        catch (Exception ex)
        {
            return Err("open_failed", ex.Message);
        }
    }

    static object SearchFacet(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var query = Opt(args, "query") ?? Opt(args, "q") ?? Opt(args, "text");
        var findWhere = where == "project" ? "project" : "external";
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = "files_desk",
            tool = ToolName,
            op = "search",
            pulse = query is { Length: > 0 }
                ? $"files · search → find_desk · {query}"
                : "files · search facet",
            cwd,
            where,
            query,
            next = new object[]
            {
                new
                {
                    go = "find_desk",
                    label = "Run find",
                    why = query is { Length: > 0 }
                        ? $"op=run what=text where={findWhere} path={cwd} query={query}"
                        : $"op=run what=text where={findWhere} path={cwd} query="
                },
                new { go = "files_desk", label = "List cwd", why = "op=list" }
            },
            hint = "FM search facet delegates to cdp_search / go=find_desk (ADR-0016). Pass query=."
        };
    }
}
