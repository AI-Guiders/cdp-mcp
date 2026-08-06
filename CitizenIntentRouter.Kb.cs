#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent kb|memory_* — in-proc memory facets (not guest MCP memory preset).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteKb(string raw)
    {
        var work = StripKbHead(raw);
        var tool = ExtractKeyedValue(work, "tool") ?? ExtractKeyedValue(work, "op");
        if (string.IsNullOrWhiteSpace(tool))
        {
            foreach (var token in work.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Contains('=', StringComparison.Ordinal))
                    continue;
                if (IsAnyKbToolToken(token))
                {
                    tool = token;
                    break;
                }
            }
        }

        var facetHint = ExtractKeyedValue(work, "facet") ?? ExtractKeyedValue(work, "server");
        var facet = ResolveKbFacet(facetHint);
        if (facet is null)
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Go: "kb", Reason: "kb_facet_unknown");

        tool = string.IsNullOrWhiteSpace(tool)
            ? DefaultToolForFacet(facet)
            : NormalizeKbTool(tool.Trim().ToLowerInvariant());

        if (!IsKbToolForFacet(tool, facet))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "kb_tool_unknown");

        if (tool is "get_process" && string.IsNullOrWhiteSpace(ExtractKeyedValue(work, "process_id")))
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_process_id_required");
        if (tool is "get_procedure" && string.IsNullOrWhiteSpace(ExtractKeyedValue(work, "procedure_id")))
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_procedure_id_required");
        if (tool is "read_knowledge_file" && string.IsNullOrWhiteSpace(ExtractKeyedValue(work, "file_path")))
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_file_path_required");

        return new Route(
            Verb.Kb,
            raw,
            Ok: true,
            Op: tool,
            Server: facet,
            Go: "kb");
    }

    /// <summary>Rewrite <c>memory_project …</c> → <c>kb facet=project …</c> (longest domain first).</summary>
    internal static bool TryRewriteMemoryDomainAlias(string raw, out string kbRaw)
    {
        foreach (var (prefix, facet) in MemoryDomainAliases)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                kbRaw = "kb facet=" + facet;
                return true;
            }

            if (raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = raw[(prefix.Length + 1)..].Trim();
                kbRaw = string.IsNullOrWhiteSpace(rest)
                    ? "kb facet=" + facet
                    : "kb facet=" + facet + " " + rest;
                return true;
            }
        }

        kbRaw = raw;
        return false;
    }

    static string StripKbHead(string raw)
    {
        if (raw.StartsWith("kb ", StringComparison.OrdinalIgnoreCase))
            return raw[3..].Trim();
        if (raw.Equals("kb", StringComparison.OrdinalIgnoreCase))
            return "";
        return raw.Trim();
    }

    static string? ResolveKbFacet(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Cdp.Core.CdpDomains.MemoryWorld;

        return raw.Trim().ToLowerInvariant() switch
        {
            "world" or "memory_world" or "kb" or "notes" => Cdp.Core.CdpDomains.MemoryWorld,
            "skill" or "memory_skill" or "ops" => Cdp.Core.CdpDomains.MemorySkill,
            "project" or "memory_project" => Cdp.Core.CdpDomains.MemoryProject,
            "session" or "memory_session" or "hot" => Cdp.Core.CdpDomains.MemorySession,
            "task" or "memory_task" or "tk" => Cdp.Core.CdpDomains.MemoryTask,
            "finding" or "findings" or "memory_self_finding" or "self_finding" => Cdp.Core.CdpDomains.MemorySelfFinding,
            "failure" or "failures" or "memory_self_failure" or "self_failure" => Cdp.Core.CdpDomains.MemorySelfFailure,
            _ => null
        };
    }

    static string DefaultToolForFacet(string facet) =>
        facet switch
        {
            Cdp.Core.CdpDomains.MemoryProject => "list_knowledge_files",
            Cdp.Core.CdpDomains.MemorySession => "memory_health",
            Cdp.Core.CdpDomains.MemorySelfFinding => "findings",
            Cdp.Core.CdpDomains.MemorySelfFailure => "failures",
            Cdp.Core.CdpDomains.MemoryTask => "route_next",
            _ => "list_pack"
        };

    static string NormalizeKbTool(string tool) =>
        tool switch
        {
            "def" or "definition" => "get_definition",
            "proc" or "process" => "get_process",
            "procedure" => "get_procedure",
            "list" or "pack" => "list_pack",
            "radius" or "gate" => "radius_gate_check",
            "read" or "file" => "read_knowledge_file",
            "tags" => "knowledge_tags",
            "files" or "ls" => "list_knowledge_files",
            "hot" or "read_hot" => "read_hot_context",
            "search" or "search_notes" => "search_agent_notes",
            "health" or "memory_health" => "memory_health",
            _ => tool
        };

    static bool IsAnyKbToolToken(string? head)
    {
        if (string.IsNullOrWhiteSpace(head) || head.Contains('=', StringComparison.Ordinal))
            return false;
        var t = NormalizeKbTool(head.Trim().ToLowerInvariant());
        return IsNotesTool(t) || IsSessionTool(t) || IsFindingTool(t) || IsFailureTool(t) || IsTaskTool(t);
    }

    static bool IsKbToolForFacet(string tool, string facet) =>
        facet switch
        {
            Cdp.Core.CdpDomains.MemoryWorld
                or Cdp.Core.CdpDomains.MemorySkill
                or Cdp.Core.CdpDomains.MemoryProject => IsNotesTool(tool),
            Cdp.Core.CdpDomains.MemorySession => IsSessionTool(tool),
            Cdp.Core.CdpDomains.MemorySelfFinding => IsFindingTool(tool),
            Cdp.Core.CdpDomains.MemorySelfFailure => IsFailureTool(tool),
            Cdp.Core.CdpDomains.MemoryTask => IsTaskTool(tool),
            _ => false
        };

    static bool IsNotesTool(string tool) =>
        tool is "get_definition" or "list_pack" or "get_process" or "get_procedure"
            or "radius_gate_check"
            or "read_knowledge_file" or "knowledge_tags" or "list_knowledge_files";

    static bool IsSessionTool(string tool) =>
        tool is "route_context" or "read_hot_context" or "memory_health"
            or "search_agent_notes" or "upsert_agent_notes_section"
            or "validate_sections" or "normalize_sections";

    static bool IsFindingTool(string tool) =>
        tool is "man" or "findings" or "finding_record" or "finding_check"
            or "tasks" or "task_record";

    static bool IsFailureTool(string tool) =>
        tool is "man" or "failures" or "failure_record";

    static bool IsTaskTool(string tool) =>
        tool is "man" or "ensure_store" or "route_next" or "tasks" or "task_upsert"
            or "read_card" or "write_card" or "upsert_section" or "analytics_upsert";

    /// <summary>Longest domain prefixes first (self_finding before self).</summary>
    static readonly (string Prefix, string Facet)[] MemoryDomainAliases =
    [
        ("memory_self_finding", "finding"),
        ("memory_self_failure", "failure"),
        ("memory_project", "project"),
        ("memory_session", "session"),
        ("memory_skill", "skill"),
        ("memory_world", "world"),
        ("memory_task", "task"),
    ];
}
