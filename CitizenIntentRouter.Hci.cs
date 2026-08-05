#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent hci|codebase_index — Hybrid Index without Cursor MCP guest.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteHci(string raw)
    {
        var head = ResolveHciHead(raw);
        var tool = ExtractKeyedValue(raw, "tool") ?? ExtractKeyedValue(raw, "op");
        if (string.IsNullOrWhiteSpace(tool) && raw.StartsWith(head + " ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw[(head.Length + 1)..].Trim();
            var sp = rest.IndexOf(' ');
            var token = sp < 0 ? rest : rest[..sp];
            if (IsHciToolHead(token))
                tool = token;
        }

        tool = string.IsNullOrWhiteSpace(tool) ? "status" : NormalizeHciTool(tool.Trim().ToLowerInvariant());
        if (!IsHciTool(tool))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "hci_tool_unknown");

        if (tool is "search" && string.IsNullOrWhiteSpace(ExtractHciQuery(raw)))
            return new Route(Verb.Hci, raw, Ok: false, Op: tool, Go: "hci", Reason: "hci_query_required");

        if (tool is "explain" && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "hit_id") ?? ExtractKeyedValue(raw, "id")))
            return new Route(Verb.Hci, raw, Ok: false, Op: tool, Go: "hci", Reason: "hci_hit_id_required");

        return new Route(
            Verb.Hci,
            raw,
            Ok: true,
            Op: tool,
            Go: "hci",
            NewString: ExtractHciQuery(raw),
            Path: ExtractKeyedValue(raw, "workspace_path")
                ?? ExtractKeyedValue(raw, "workspace")
                ?? ExtractKeyedValue(raw, "path"));
    }

    static string ResolveHciHead(string raw)
    {
        if (raw.StartsWith("codebase_index", StringComparison.OrdinalIgnoreCase))
            return "codebase_index";
        if (raw.StartsWith("hybrid_index", StringComparison.OrdinalIgnoreCase))
            return "hybrid_index";
        if (raw.StartsWith("cdp_hci", StringComparison.OrdinalIgnoreCase))
            return "cdp_hci";
        return "hci";
    }

    static string NormalizeHciTool(string tool) =>
        tool switch
        {
            "codebase_index_search" or "find" or "q" => "search",
            "codebase_index_status" or "scene" or "pulse" => "status",
            "codebase_index_reindex" or "rebuild" or "index" => "reindex",
            "codebase_index_explain" => "explain",
            "codebase_index_version" or "ver" => "version",
            "codebase_index_man" => "man",
            _ => tool
        };

    static bool IsHciToolHead(string? head)
    {
        if (string.IsNullOrWhiteSpace(head) || head.Contains('=', StringComparison.Ordinal))
            return false;
        return IsHciTool(NormalizeHciTool(head.Trim().ToLowerInvariant()));
    }

    static bool IsHciTool(string? tool) =>
        tool is "search" or "status" or "reindex" or "explain" or "version" or "man"
            or "codebase_index_search" or "codebase_index_status" or "codebase_index_reindex"
            or "codebase_index_explain" or "codebase_index_version" or "codebase_index_man"
            or "find" or "q" or "scene" or "pulse" or "rebuild" or "index" or "ver";

    static string? ExtractHciQuery(string raw) =>
        ExtractKeyedValue(raw, "query")
        ?? ExtractKeyedValue(raw, "q")
        ?? ExtractKeyedValue(raw, "text")
        ?? ExtractKeyedValue(raw, "pattern");
}
