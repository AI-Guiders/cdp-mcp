namespace CdpMcp;

/// <summary>MCP server instructions string (≤ADX soft-warn peel).</summary>
internal static partial class ProgramHost
{
    internal const string ServerInstructions =
        "Cognitive Dev Platform = agent-IDE substrate (not pixel IDE). " +
        "catalog=f(phase,object[,language]); intent ranks. " +
        "Lifecycle: recall → explore → clarify → plan → act → verify → handoff. " +
        "Cold ListTools = recall+kb (known memory pull; not browse). " +
        "After MCP restart: call cdp_session or cdp_context first so ListTools refreshes (pack tools). " +
        "Pack dogfood: memory_world_get_definition|get_process|get_procedure|list_pack|radius_gate_check (epistemic-scene). " +
        "Always: cdp_cockpit (desk seats P|F|M + cmd= REPL: next[]+go=) / cdp_session (omnibus) / cdp_context / cdp_open / cdp_restore (Restore Previous desk) / cdp_deploy (dual-instance publish; go=deploy) / cdp_land (Family:navigation Anchor land) / cdp_mcp (MCP outlet scene/mount/call) / cdp_browser (internet lynx: scene_internet_browser) / cdp_settings (Tools→Options: go=options) / cdp_editor_scene|cdp_edit_plan / cdp_buffer(op) / cdp_debug(op) / cdp_recent / cdp_build|cdp_run|cdp_test / cdp_pkg_* / cdp_work (intent scenes) / cdp_tools (palette) / cdp_health (explain_tool?). " +
        "Mutate SSOT: cdp_buffer (open|create|edit); Instant Save flush=true on edit/close (flush=false batches; close discard=true to drop). Relative path= → ProjectRoot after cdp_open. Prefer edit_op=anchor [F:;M:;K:] for csharp. Cursor host Write bypasses PathMutateGate. " +
        "Buffer plane: cdp_buffer op=open|edit|… — edit returns diagnostics in-result (almost-online while you keep the turn). " +
        "Debug plane: cdp_debug op=bp_add|launch|stop_context|… — session defaults after cdp_open; .csproj is BP key, launch resolves dll under bin/; JSON file is storage only. " +
        "IDE verbs (harness routes LSP): go_to_definition, find_usages, get_document_symbols, get_symbol_at_position, get_diagnostics, resolve_project_root, get_workspace_navigation_context. " +
        "Prefer cdp_build/cdp_run/cdp_test/cdp_pkg_*/cdp_project_*/cdp_sln_* over shell for session project. " +
        "Agent shell habitat: cdp_shell_* = primary IDE terminal; sibling terminal-mcp (terminal_*) = escape only. " +
        "CSX: cdp_script_scene (put→diags→check→run) | cdp_csx_help | cdp_csx_check | cdp_csx_run | cdp_csx_run_plan | promote | discard | cdp_evidence. " +
        "PS1: cdp_ps1_scene (ISE put→AST check→pwsh -File→last). " +
        "Domain tools prefixed memory_world_|memory_project_|memory_task_|memory_session_|memory_skill_|memory_self_finding_|memory_self_failure_|debug_|build_|roslyn_|git_|codebase_index_|anui_ (roslyn_* = legacy aliases; prefer bare IDE verbs). " +
        "ListTools = core meta + bare IDE verbs + ≤10 domain shortlist (soft-organ Metas via go=/CallTool; not always-ListTools). " +
        "Too many tools = agent thrash — use cdp_context to retarget, cdp_tools to preview, cdp_session (A; include_pack=true only when needed). " +
        "Continuity: route/handoff before deep topic; evidence-first (stop_context), PNG last.";
}
