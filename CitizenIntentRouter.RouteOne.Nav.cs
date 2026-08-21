#nullable enable

namespace CdpMcp;

/// <summary>RouteOne family gate: Nav — peel method_lines off RouteOne.</summary>
internal static partial class CitizenIntentRouter
{
    static Route? TryRouteNav(string raw)
    {
        if (raw.Equals("edit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("anchor", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("anchor ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("anchor path=", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferEdit.Route(raw);
        }

        if (raw.Equals("deploy", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("deploy ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("deploy mode=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("hard_deploy", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("hard_deploy ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("soft_deploy", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("soft_deploy ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteDeploy(raw);
        }

        if (raw.Equals("undo", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("undo ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("undo path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("redo", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("redo ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("redo path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("edit_history", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit_history ", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferUndo.Route(raw);
        }

        if (raw.Equals("copy", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("copy ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("copy path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cut", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cut ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cut path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("paste", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("paste ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("paste path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clipboard", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("clipboard ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clip", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("clip ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clipboard_clear", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clip_clear", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferClip.Route(raw);
        }

        if (raw.Equals("back", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("back ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("forward", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("forward ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nav", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nav ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nav_status", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("recent_files", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("recent_files ", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferNav.Route(raw);
        }

        if (raw.Equals("find_all", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find_all ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("findall", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("findall ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buf_find", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_find", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find_in", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find_buffer", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buf_find_all", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_find_all", StringComparison.OrdinalIgnoreCase))
        {
            return CitizenBufferFindBuf.Route(raw);
        }

        if (raw.Equals("find", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find query=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("search", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("search ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("search query=", StringComparison.OrdinalIgnoreCase))
        {
            if (CitizenBufferFindBuf.LooksLikeBufferFindScope(raw))
                return CitizenBufferFindBuf.Route(raw);
            return RouteFind(raw);
        }

        if (LooksLikeGotoAll(raw))
            return RouteGotoAll(raw);

        if (raw.Equals("ide", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ide ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("goto", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("goto ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("usages", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("usages ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("diagnostics ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("definition", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("definition ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("complete", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("complete ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("completions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("completions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("signature", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("signature ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("signature_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("signature_help ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbols ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("document_symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("document_symbols ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("doc_symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_symbols ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("symbol", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbol ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("hover", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("hover ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("symbol_at", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbol_at ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("rename", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rename ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("actions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("actions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("code_actions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("code_actions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quickfix", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quickfix ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("apply_action", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("apply_action ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("apply_code_action", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("apply_code_action ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("related", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("related ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("semantic_map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("semantic_map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nav_context", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nav_context ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("workspace_nav", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("workspace_nav ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("subgraph", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("subgraph ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("project_root", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_root ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("resolve_root", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("resolve_root ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("resolve_project_root", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("resolve_project_root ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("workspace_root", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("workspace_root ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteIde(raw);
        }

        return null;
    }
}
