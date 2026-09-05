#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>cdp_man TOC / tool blurb for MetaDispatch.Core (method_lines peel).</summary>
internal static partial class MetaDispatch
{
    static string ManText(IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        if (callArgs.TryGetValue("tool", out var t) && t.GetString() is { Length: > 0 } tool)
        {
            if (tool is "context_budget" or "budget" or "context")
                return SessionPlane.ContextBudgetManual;
                return SessionPlane.ContextBudgetManual;
            if (tool is "cdp_buffer" or "buffer")
                return BufferManual;
            return $"Manual: {tool} — see tool description; domain ops via prefixed tools / sibling man.";
        }

        return "TOC: cdp_cockpit (hub where-am-I), cdp_session (A omnibus; include_pack=true for pack dogfood), cdp_health(explain_tool?), cdp_capabilities, " +
               "cdp_context(phase,object,intent?,language?), cdp_open(path), cdp_peek(path|paths[]|query|anchor — read-only eyes, ADR-0201), cdp_editor_scene|cdp_edit_sniper|cdp_edit_plan (map→aim→slices), " +
               "cdp_build|cdp_run|cdp_test|cdp_test_scene|cdp_test_plan (session IDE lifecycle), " +
               "cdp_analysis_scene (code analysis domain; feature=clones), " +
               "cdp_script_scene (script habitat put→diags→run), " +
               "cdp_ps1_scene (PS ISE put→check→run), " +
               "cdp_goto (Ctrl+T code + Ctrl+Q features → land/peek), " +
               "cdp_buffer(op=scene|open|read|edit|diagnostics|close) file buffer SSOT; edit returns diagnostics, " +
               "cdp_debug(op=scene|bp_add|bp_remove|bp_set|bp_list|bp_clear|launch|…) debug plane; session defaults, not breakpoints JSON, " +
               "cdp_pkg_find|list|add|remove|update|outdated|audit|latest|upgrade_plan|fix_vuln|supply_chain, cdp_project_scene|create|list|close|add_to_sln, " +
               "cdp_sln_create|list|projects|add|remove, " +
               "cdp_work(op=intent|stage|scene), cdp_tools(... palette), " +
               "IDE: go_to_definition|find_usages|get_document_symbols|get_symbol_at_position|get_diagnostics|get_completions|get_signature_help|find|find_in_files|take|resolve_project_root|get_workspace_navigation_context, " +
               "cdp_csx_help / cdp_csx_check / cdp_csx_run / cdp_csx_run_plan / promote / discard. " +
               "cdp_shell_scene|run|history|rerun|last|which|kill|close (agent terminal; background long-run). " +
               "Pack: get_definition|list_pack|get_process|get_procedure|radius_gate_check. " +
               "Domain prefixes: memory_world_ memory_project_ memory_task_ memory_session_ memory_skill_ " +
               "memory_self_finding_ memory_self_failure_ debug_ build_ roslyn_ git_ codebase_index_ anui_. " +
               "Agent-IDE pillars: session plane, shared truth, affordance nav, continuity, evidence-first, self-ops. " +
               "Order: Agent Env first; CIDE projector later. " +
               "Context: man tool=context_budget (EICAS W/C/A).";
    }

    /// <summary>cdp_buffer manual — address model canon + known corner cases (2026-09-05).</summary>
    internal const string BufferManual =
        "cdp_buffer — file buffer SSOT. op=scene|open|read|take|edit|diagnostics|close (+ undo/history/find…). " +
        "Edit ops: set_text (whole file; force=true to overwrite existing disk content, allow_shrink when shorter), " +
        "replace (old_string→new_string; multi-line old_string NOT resolved — single-line only), " +
        "replace_range (line/col span; end exclusive; end position clamps to line end / EOF), " +
        "anchor ([F:;M:;K:] / [F:;X:;A:] + place=before|after|into|end|replace). " +
        "Address canon: semantic anchors are THE address — C# M:/K:, xml X:/A:. " +
        "L: (line_literal) is a FALLBACK facet — for non-semantic files (md/json/toml/yaml) and peek chains (lines[].anchor). " +
        "Line-number edits on code = model degradation (Cursor-coordinates regress) — prefer member anchors (2026-07-23 decision). " +
        "F#: T: needles FORBIDDEN on .fs (silently resolve to file-scope — whole-file wipe risk); prefer M: replace or set_text. " +
        "Fixed 2026-09-05: replace_range end CLAMPS (LSP-style) — end_line beyond buffer = EOF, end_column beyond line = whole line (no tail garbage); start stays strict. " +
        "line_count = physical lines everywhere (trailing newline adds none; buffer meta == peek total_lines).";}


