#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent kb|memory_* — in-proc memory facets (not guest MCP memory preset).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteKb(string raw)
    {
        var work = StripKbHead(raw);
        var facetHint = ExtractKeyedValue(work, "facet") ?? ExtractKeyedValue(work, "server");
        string? facet;
        if (!string.IsNullOrWhiteSpace(facetHint))
        {
            facet = ResolveKbFacet(facetHint);
            if (facet is null)
                return new Route(Verb.Kb, raw, Ok: false, Op: null, Go: "kb", Reason: "kb_facet_unknown");
        }
        else
        {
            // Lived SoftFL: "kb memory_session memory_health" → bare memory_session ignored,
            // facet defaulted world → kb_tool_unknown (pulse memory_world … unknown).
            work = TryConsumeBareFacetToken(work, out facet);
            facet ??= Cdp.Core.CdpDomains.MemoryWorld;
        }

        // Prefer positional known tool — keyed tool=/op= is often an ARG
        // (e.g. failure_record tool=cdp_test; man tool=findings). SoftFL invent when arg steals Op.
        string? positional = null;
        foreach (var token in work.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Contains('=', StringComparison.Ordinal))
                continue;
            if (IsAnyKbToolToken(token))
            {
                positional = token;
                break;
            }
        }

        var keyed = ExtractKeyedValue(work, "tool") ?? ExtractKeyedValue(work, "op");
        var tool = !string.IsNullOrWhiteSpace(positional) ? positional : keyed;

        var freeQuery = ExtractKeyedValue(work, "query") ?? ExtractKeyedValue(work, "q");
        var filePathHint = ExtractKeyedValue(work, "file_path") ?? ExtractKeyedValue(work, "path");
        if (string.IsNullOrWhiteSpace(tool) && !string.IsNullOrWhiteSpace(freeQuery))
        {
            // Free-text dig → agent-notes search (not silent list_pack / epistemic-scene dump).
            tool = "search_agent_notes";
            facet = Cdp.Core.CdpDomains.MemorySession;
        }
        else if (string.IsNullOrWhiteSpace(tool) && LooksLikeKnowledgeFilePath(filePathHint))
        {
            // Lived: operator gave knowledge/… path; bare kb → list_pack; read without shape → missing.
            tool = "read_knowledge_file";
        }
        else
        {
            tool = string.IsNullOrWhiteSpace(tool)
                ? DefaultToolForFacet(facet)
                : NormalizeKbTool(tool.Trim().ToLowerInvariant());
        }

        // search_* lives on memory_session — not world/skill pack tools (thin refuse → SoftFL invent).
        if (tool is "search_agent_notes" or "upsert_agent_notes_section"
            or "validate_sections" or "normalize_sections")
            facet = Cdp.Core.CdpDomains.MemorySession;

        // Lived SoftFL: "kb findings" / "kb failures" / "kb health" → tool head skips bare-facet
        // consume → default world → kb_tool_unknown. Exclusive tools imply their facet
        // (same class as search_* / task exclusives). Ambiguous man/tasks stay world-default.
        if (tool is "findings" or "finding_record" or "finding_check")
            facet = Cdp.Core.CdpDomains.MemorySelfFinding;
        if (tool is "failures" or "failure_record")
            facet = Cdp.Core.CdpDomains.MemorySelfFailure;
        if (tool is "memory_health" or "route_context" or "read_hot_context")
            facet = Cdp.Core.CdpDomains.MemorySession;

        // TaskKnowledge exclusive tools live on memory_task — wrong facet → SoftFL invent "unknown".
        // Do not remap ambiguous "tasks"/"man" (also findings).
        if (tool is "ensure_store" or "route_next" or "task_upsert"
            or "read_card" or "write_card" or "upsert_section" or "analytics_upsert")
            facet = Cdp.Core.CdpDomains.MemoryTask;

        if (!IsKbToolForFacet(tool, facet))
            // Keep Op/Server so host can tip pulse (pulse=null → SoftFL invent "missing tool").
            return new Route(Verb.Unknown, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_tool_unknown");

        if (tool is "get_process" && string.IsNullOrWhiteSpace(ExtractKeyedValue(work, "process_id")))
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_process_id_required");
        if (tool is "get_procedure" && string.IsNullOrWhiteSpace(ExtractKeyedValue(work, "procedure_id")))
            return new Route(Verb.Kb, raw, Ok: false, Op: tool, Server: facet, Go: "kb", Reason: "kb_procedure_id_required");
        if (tool is "read_knowledge_file"
            && string.IsNullOrWhiteSpace(ExtractKeyedValue(work, "file_path"))
            && string.IsNullOrWhiteSpace(ExtractKeyedValue(work, "path")))
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

/// <summary>Bare facet after kb head (memory_session|session|…) — not a tool token.</summary>
    static string TryConsumeBareFacetToken(string work, out string? facet)
    {
        facet = null;
        if (string.IsNullOrWhiteSpace(work))
            return work;

        var tokens = work.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return work;

        var head = tokens[0];
        if (head.Contains('=', StringComparison.Ordinal))
            return work;
        if (IsAnyKbToolToken(head))
            return work;

        var resolved = ResolveKbFacet(head);
        // ResolveKbFacet(null/blank) → world; blank head already refused above.
        // Unknown alias → null — leave work unchanged.
        if (resolved is null)
            return work;

        facet = resolved;
        return tokens.Length == 1 ? "" : string.Join(' ', tokens[1..]);
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

    /// <summary>Operator/colloquial KB path — bind read (host strips leading knowledge/).</summary>
    internal static bool LooksLikeKnowledgeFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var n = path.Replace('\\', '/').Trim().TrimStart('/');
        if (n.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(n))
            return false;
        return n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("knowledge/", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("worlds/", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("domains/", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("META/", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("work/", StringComparison.OrdinalIgnoreCase);
    }

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
