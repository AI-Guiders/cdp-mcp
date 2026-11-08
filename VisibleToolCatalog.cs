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
        // meta / soft scenes superseded by cdp_graphql.
        // cdp_peek возвращён в листинг 2026-10-27: базовая поверхность чтения
        // (ADR-0201), не ситуационный read — Code Mode-агент вне листинга его
        // не видит; схема генерируется из LineEntry (ToolSchemas).
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
        // knowledge-read → knowledge { recall|tags|read|list|definition|procedure|process|packs|radiusGate }; write stays
        "memory_world_knowledge_tags",
        "memory_world_read_knowledge_file",
        "memory_world_list_knowledge_files",
        "memory_world_recall_knowledge",
        "memory_world_get_definition",
        "memory_world_get_procedure",
        "memory_world_get_process",
        "memory_world_list_pack",
        "memory_world_radius_gate_check",
        // lifecycle pulses → lastBuild|lastTest|lifecycle
        "cdp_lifecycle_scene",
        "cdp_lifecycle_last",
        // situational → health|recent|projectScene|… + project{}/sln{}
        "cdp_health",
        "cdp_recent",
        "cdp_project_scene",
        "cdp_project_list",
        "cdp_sln_list",
        "cdp_sln_projects",
        "cdp_editor_scene",
        "cdp_canon_stack",
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
        "cdp_cide_presentation",
        "cdp_peel"
    };

    public static List<Tool> Build(VisibleToolCatalogDeps d)
    {
        var meta = d.BuildMetaTools()
            .Where(t => !SoftInstrumentMetaNames.Contains(t.Name)
                && !GraphQlCollapsedReadNames.Contains(t.Name)
                && IsMetaOrIdeVisibleInSession(t.Name, d.Session))
            .ToList();
        var ide = IdeLanguageTools.BuildBareVerbTools()
            .Where(t => !GraphQlCollapsedReadNames.Contains(t.Name)
                && IsMetaOrIdeVisibleInSession(t.Name, d.Session))
            .ToList();
        var hits = PhaseObjectCatalog.Query(
            d.AllAffordances, d.Session.Phase, d.Session.Object, d.Session.Intent,
            limit: PhaseObjectCatalog.DefaultListToolsLimit, language: d.Session.Language);
        var domainTools = new List<Tool>();
        foreach (var hit in hits)
        {
            var a = hit.Affordance;
            if (GraphQlCollapsedReadNames.Contains(a.PrefixedName))
                continue;
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
                InputSchema = schema,
                // Facade-declared result contracts (e.g. AgentNotes string
                // reports) ride through — code-mode clients render them as
                // typed signatures instead of an opaque unknown.
                OutputSchema = schemaTool.OutputSchema
            });
        }
        return meta.Concat(ide).Concat(domainTools).ToList();
    }

    /// <summary>
    /// Phase-gate Meta and IDE bare verbs so ListTools stays a focused ~22–35 tool palette
    /// per (Phase, Object) instead of dumping all 61 Meta+IDE tools in every phase.
    /// Every tool remains callable via CallTool regardless of phase.
    /// </summary>
    public static bool IsMetaOrIdeVisibleInSession(string name, SessionContext s)
    {
        var phase = s.Phase;
        var obj = s.Object;

        return name switch
        {
            // Shell plumbing (cdp_shell_run stays always-on in core): only on Process/Session
            "cdp_shell_close" or "cdp_shell_history" or "cdp_shell_kill" or "cdp_shell_last"
                or "cdp_shell_rerun" or "cdp_shell_scene" or "cdp_shell_which"
                => obj is CdpObjectKind.Process or CdpObjectKind.Session || phase == CdpPhase.Handoff,

            // Host / settings / low-level scene inspection: only on Process/Session
            "cdp_capabilities" or "cdp_cockpit_host" or "cdp_ide_session_scene"
                or "cdp_mcp" or "cdp_settings"
                => obj is CdpObjectKind.Process or CdpObjectKind.Session || phase == CdpPhase.Handoff,

            // Package & solution/project scaffolding: only in Plan / Act on Code / Repo
            "cdp_pkg_add" or "cdp_pkg_fix_vuln" or "cdp_pkg_remove" or "cdp_pkg_update"
                or "cdp_project_add_to_sln" or "cdp_project_close" or "cdp_project_create"
                or "cdp_sln_add" or "cdp_sln_create" or "cdp_sln_remove"
                => phase is CdpPhase.Act or CdpPhase.Plan && obj is CdpObjectKind.Code or CdpObjectKind.Repo,

            // Worktree CSX multi-step plans (cdp_csx_check and cdp_csx_run stay always-on in core)
            "cdp_csx_run_plan" or "cdp_csx_help" or "cdp_script_scene"
                => phase is CdpPhase.Act or CdpPhase.Plan,
            "cdp_csx_promote" or "cdp_csx_discard"
                => phase is CdpPhase.Act or CdpPhase.Verify or CdpPhase.Review or CdpPhase.Handoff,

            // Visual / UI / Evidence / Test-plan organs
            "cdp_browser" or "cdp_see" or "cdp_evidence" or "take" or "get_take" or "cdp_test_plan"
                => phase is CdpPhase.Explore or CdpPhase.Verify or CdpPhase.Review
                    || obj is CdpObjectKind.Issue or CdpObjectKind.Process,

            // LSP code actions / refactoring: visible whenever working with Code/Repo (not cold Recall/Kb)
            "code_actions" or "apply_code_action" or "rename_symbol" or "cdp_edit_sniper" or "resolve_project_root"
                => obj is CdpObjectKind.Code or CdpObjectKind.Repo
                    || phase is CdpPhase.Explore or CdpPhase.Act or CdpPhase.Review,

            // Low-level editor completions: only in Act on Code
            "get_completions" or "get_signature_help"
                => phase == CdpPhase.Act && obj == CdpObjectKind.Code,

            // Core desk & hands (cdp_open, cdp_context, cdp_peek, cdp_graphql, cdp_suggest,
            // cdp_buffer, cdp_edit_plan, cdp_build, cdp_test, cdp_run, cdp_debug, cdp_deploy,
            // cdp_csx_check, cdp_csx_run, cdp_shell_run, cdp_man, cdp_tools, cdp_cockpit,
            // cdp_work, cdp_session, cdp_restore): always visible.
            _ => true
        };
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
