#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent kb — in-proc agent-notes pack/KB (not guest MCP memory preset).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteKb(string raw)
    {
        var tool = ExtractKeyedValue(raw, "tool") ?? ExtractKeyedValue(raw, "op");
        if (string.IsNullOrWhiteSpace(tool) && raw.StartsWith("kb ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw["kb ".Length..].Trim();
            var sp = rest.IndexOf(' ');
            var head = sp < 0 ? rest : rest[..sp];
            if (IsKbTool(head))
                tool = head;
        }

        tool = string.IsNullOrWhiteSpace(tool) ? "list_pack" : tool.Trim().ToLowerInvariant();
        if (tool is "def" or "definition")
            tool = "get_definition";
        else if (tool is "proc" or "process")
            tool = "get_process";
        else if (tool is "procedure")
            tool = "get_procedure";
        else if (tool is "list" or "pack")
            tool = "list_pack";
        else if (tool is "radius" or "gate")
            tool = "radius_gate_check";
        else if (tool is "read" or "file")
            tool = "read_knowledge_file";
        else if (tool is "tags")
            tool = "knowledge_tags";
        else if (tool is "files" or "ls")
            tool = "list_knowledge_files";

        if (!IsKbTool(tool))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "kb_tool_unknown");

        var facet = ResolveKbFacet(ExtractKeyedValue(raw, "facet") ?? ExtractKeyedValue(raw, "server"));
        if (facet is null)
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Go: "kb", Reason: "kb_facet_unknown");

        if (tool is "get_process" && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "process_id")))
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_process_id_required");
        if (tool is "get_procedure" && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "procedure_id")))
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_procedure_id_required");
        if (tool is "read_knowledge_file" && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "file_path")))
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_file_path_required");

        return new Route(
            Verb.Kb,
            raw,
            Ok: true,
            Op: tool,
            Server: facet,
            Go: "kb");
    }

    static string? ResolveKbFacet(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Cdp.Core.CdpDomains.MemoryWorld;

        return raw.Trim().ToLowerInvariant() switch
        {
            "world" or "memory_world" or "kb" or "notes" => Cdp.Core.CdpDomains.MemoryWorld,
            "skill" or "memory_skill" or "ops" => Cdp.Core.CdpDomains.MemorySkill,
            _ => null
        };
    }

    static bool IsKbTool(string? tool) =>
        tool is "get_definition" or "list_pack" or "get_process" or "get_procedure"
            or "radius_gate_check"
            or "read_knowledge_file" or "knowledge_tags" or "list_knowledge_files"
            or "def" or "definition" or "proc" or "process" or "procedure"
            or "list" or "pack" or "radius" or "gate" or "read" or "file" or "tags"
            or "files" or "ls";
}
