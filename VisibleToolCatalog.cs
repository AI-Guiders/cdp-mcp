#nullable enable
using System.Text.Json;
using Cdp.Core;
using ModelContextProtocol.Protocol;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

/// <summary>ListTools composition peeled from Program (soft-warn).</summary>
internal static class VisibleToolCatalog
{
    /// <summary>
    /// ADR-0233 L3 hard collapse — situational read leaves ListTools; engines stay CallTool
    /// for <c>cdp_graphql</c> / MetaDispatch. Mutate bare verbs stay listed.
    /// </summary>
    public static readonly HashSet<string> GraphQlCollapsedReadNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // bare read → textHits / peek / goto / diagnostics / session
        "find",
        "get_find",
        "find_in_files",
        "find_all",
        "go_to_definition",
        "find_usages",
        "get_document_symbols",
        "get_symbol_at_position",
        "get_diagnostics",
        "get_workspace_navigation_context",
        // meta / soft scenes superseded by cdp_graphql
        "cdp_peek",
        "cdp_search",
        "cdp_goto",
        "cdp_analysis_scene",
        "cdp_test_scene",
        // pkg-read → packages { list|find|… }; mutate stays listed
        "cdp_pkg_find",
        "cdp_pkg_list",
        "cdp_pkg_outdated",
        "cdp_pkg_audit",
        "cdp_pkg_latest",
        "cdp_pkg_upgrade_plan",
        "cdp_pkg_supply_chain",
        // git-read → git { scene|status|diff|preflight }; commit/push stay
        "git_git_scene",
        "git_status",
        "git_diff",
        "git_preflight",
        // knowledge-read → knowledge { recall|tags|read|list }; write stays
        "memory_world_knowledge_tags",
        "memory_world_read_knowledge_file",
        "memory_world_list_knowledge_files",
        // lifecycle pulses → lastBuild|lastTest|lifecycle
        "cdp_lifecycle_scene",
        "cdp_lifecycle_last",
    };

    /// <summary>Soft organs with go= aliases — CallTool ok, omit from always-ListTools.</summary>
    public static readonly HashSet<string> SoftInstrumentMetaNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdp_search",
        "cdp_sa",
        "cdp_refactor",
        "cdp_debug_sa",
        "cdp_test_sa",
        "cdp_build_sa",
        "cdp_ship",
        "cdp_crm",
        "cdp_arch",
        "cdp_onboard",
        "cdp_toolchain",
        "cdp_files",
        "cdp_md_author",
        "cdp_learn",
        "cdp_domain",
        "cdp_rules",
        "cdp_inventory",
        "cdp_verify_wave",
        "cdp_fdr",
        "cdp_teeth",
        "cdp_postmortem",
        "cdp_scope",
        "cdp_webcam",
        "cdp_glass",
        "cdp_ps1_scene",
        // L4 amortise — soft instruments stay CallTool/go= reachable, omit always-ListTools
        "cdp_ignite",
        "cdp_pressure",
        "cdp_calendar",
        "cdp_ground",
        "cdp_freshness",
        "cdp_env_readiness",
        "cdp_ide_health",
        "cdp_icm",
        "cdp_forum",
        "cdp_intercom",
        "cdp_citizen",
        "cdp_elicit",
        "cdp_land",
        "cdp_cide_presentation"
    };

    public static List<Tool> Build(VisibleToolCatalogDeps d)
    {
        var meta = d.BuildMetaTools()
            .Where(t => !SoftInstrumentMetaNames.Contains(t.Name)
                && !GraphQlCollapsedReadNames.Contains(t.Name))
            .ToList();
        var ide = IdeLanguageTools.BuildBareVerbTools()
            .Where(t => !GraphQlCollapsedReadNames.Contains(t.Name))
            .ToList();
        var hits = PhaseObjectCatalog.Query(
            d.AllAffordances, d.Session.Phase, d.Session.Object, d.Session.Intent,
            limit: PhaseObjectCatalog.DefaultListToolsLimit, language: d.Session.Language);
        var domainTools = new List<Tool>();
        foreach (var hit in hits)
        {
            var a = hit.Affordance;
            var schemaTool = ResolveSchema(d, a.Domain, a.UnderlyingName);
            if (schemaTool is null) continue;
            var schema = a.Domain == CdpDomains.Git
                ? GitSessionDefaults.OptionalWorkspaceSchema(schemaTool.InputSchema)
                : a.Domain == CdpDomains.CodebaseIndex
                ? CodebaseIndexSessionDefaults.OptionalSessionSchema(schemaTool.InputSchema)
                : a.Domain == CdpDomains.Build
                ? BuildSessionDefaults.OptionalSessionSchema(schemaTool.InputSchema)
                : MemorySessionDefaults.IsMemoryDomain(a.Domain)
                ? MemorySessionDefaults.OptionalWorkspaceSchema(schemaTool.InputSchema)
                : schemaTool.InputSchema;
            domainTools.Add(new Tool
            {
                Name = a.PrefixedName,
                Description = $"[{a.Domain}] {schemaTool.Description}",
                InputSchema = schema
            });
        }
        return meta.Concat(ide).Concat(domainTools).ToList();
    }

    static Tool? ResolveSchema(VisibleToolCatalogDeps d, string domain, string underlying) => domain switch
    {
        CdpDomains.MemoryWorld or CdpDomains.MemoryProject or CdpDomains.MemorySkill or CdpDomains.MemorySession
            => d.AnTools.GetValueOrDefault(underlying),
        CdpDomains.MemoryTask => d.TkTools.GetValueOrDefault(underlying),
        CdpDomains.MemorySelfFinding => d.FindTools.GetValueOrDefault(underlying),
        CdpDomains.MemorySelfFailure => d.FailTools.GetValueOrDefault(underlying),
        CdpDomains.Debug => d.DbgTools.GetValueOrDefault(underlying),
        CdpDomains.Build => d.BtTools.GetValueOrDefault(underlying),
        CdpDomains.Roslyn => d.RoslynTools.GetValueOrDefault(underlying),
        CdpDomains.Git => d.GitTools.GetValueOrDefault(underlying),
        CdpDomains.CodebaseIndex => d.HciTools.GetValueOrDefault(underlying),
        CdpDomains.Anui => d.AnuiTools.GetValueOrDefault(underlying),
        _ => null
    };
}

internal sealed class VisibleToolCatalogDeps
{
    public required SessionContext Session { get; init; }
    public required ToolAffordance[] AllAffordances { get; init; }
    public required Func<List<Tool>> BuildMetaTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> AnTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> TkTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> FindTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> FailTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> DbgTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> BtTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> RoslynTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> GitTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> HciTools { get; init; }
    public required IReadOnlyDictionary<string, Tool> AnuiTools { get; init; }
}
