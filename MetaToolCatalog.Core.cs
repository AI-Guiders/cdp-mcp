#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog — Core session/edit (man→debug); ops peel in Core.Ops.</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> Core() =>
    [
    Meta("cdp_man", "[A] CDP ops manual. tool= omit for TOC; or context_budget|cdp_health|cdp_capabilities|cdp_context|cdp_tools|cdp_session|cdp_shell_*.", new
    {
        type = "object",
        properties = new { tool = new { type = "string" } }
    }),
    Meta("cdp_health", "[A] Backend health + runtime (version/exe/build_utc/pending_update). Optional explain_tool=prefixed name → why missing from shortlist.", new
    {
        type = "object",
        properties = new
        {
            explain_tool = new { type = "string", description = "Prefixed tool name to explain visibility." }
        }
    }),
    Meta("cdp_capabilities", "Mounted domains + layers.memory facets/roots + affordance counts.", new { type = "object", properties = new { } }),
    Meta("cdp_context", "[A] Get/set session phase+object(+intent[+language]). Phase change auto-applies desk layout (SA). Hold: layout_hold= or desk.layout.hold. Triggers tools/list_changed.", new
    {
        type = "object",
        properties = new
        {
            phase = new { type = "string", description = "recall|explore|clarify|plan|act|verify|handoff — also retunes desk seats unless hold" },
            @object = new { type = "string", description = "kb|code|repo|task|finding|process|issue|session" },
            intent = new { type = "string", description = "optional find|cite|change|verify|record|ship" },
            language = new { type = "string", description = "optional language id/alias from [languages] config; empty clears" },
            layout_hold = new { type = "boolean", description = "Skip phase→desk auto-layout this call (or set desk.layout.hold)" },
            get = new { type = "boolean", description = "If true, only return current context." }
        }
    }),
    Meta("cdp_open", "Open a project path: detect .sln/.csproj/tsconfig → session root+language+scm_root; list_changed. After open, git_*/codebase_index_*/memory_*/build_* may omit workspace_path/solution_path (session defaults). Prefer before go_to_definition. Omit path to reopen Recent[0]; or recent_index=N. Autosaves desk bookmark for cdp_restore.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "File or directory (.sln, .csproj, tsconfig.json, source file, or folder). Optional if recent_index set or Recent non-empty." },
            recent_index = new { type = "integer", description = "Optional 0-based Open Recent index (0 = last opened)." }
        }
    }),
    Meta("cdp_buffer", "File buffer plane: op=scene|open|create|put|take|share|read|edit|diagnostics|close|reload|keep_disk|disk_peek + comfort undo|redo|history|copy|cut|paste|clipboard|find|…. put= dump draft; share with=operator|self (inbox/shelf + thin chat); share from=self|latest (pull shelf body into tool result); take= file span into agent (rare). Instant Save. Anchors: edit_op=anchor + place=before|after|replace (default replace). Relative path= → ProjectRoot.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|open|create|put|take|read|edit|diagnostics|close|reload|keep_disk|disk_peek|undo|redo|history|copy|cut|paste|clipboard|clipboard_clear|find|find_all|replace_all|back|forward|nav|recent_files|scratch" },
            path = new { type = "string", description = "reload|keep_disk|disk_peek: optional (omit = all drifted); find scope=project: optional subdir; find scope=external: required absolute root; otherwise file path" },
            pad = new { type = "integer", description = "disk_peek: ± context lines around first diff (default 2)" },
            doc_id = new { type = "string" },
            diagnose = new { type = "boolean", description = "open default false; create/edit default true (csharp: syntax)" },
            flush = new { type = "boolean", description = "edit/close/undo/redo/paste default true (Instant Save). false = keep dirty in memory (batch)." },
            discard = new { type = "boolean", description = "close only: with flush=false, required to drop dirty buffer without writing." },
            refresh = new { type = "boolean", description = "open: reload from disk; diagnostics: soft prefer-cache when false" },
            force = new { type = "boolean", description = "diagnostics: recompute even if version unchanged" },
            scope = new { type = "string", description = "diagnostics: syntax|project|solution; find: buffer|project|files|external (default buffer)" },
            overwrite = new { type = "boolean", description = "create: allow replace existing file" },
            allow_shrink = new { type = "boolean", description = "edit set_text: required when new body is shorter than on-disk file" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" },
            edit_op = new { type = "string", description = "edit: anchor|set_text|replace|replace_range — prefer anchor" },
            anchor = new { type = "string", description = "edit_op=anchor / copy|cut|paste: csharp [F:;M:;K:] or xml [F:;X:path;A:attr?][+K:Element]" },
            at = new { type = "string", description = "Alias of anchor" },
            text = new { type = "string", description = "edit set_text / create body / anchor text (replace=overwrite locus; place=before|after=insert body) / paste override / find query alias" },
            old_string = new { type = "string" },
            new_string = new { type = "string", description = "replace; also alias of text for anchor" },
            start_column = new { type = "integer" },
            end_column = new { type = "integer" },
            query = new { type = "string", description = "find|find_all|replace_all needle" },
            pattern = new { type = "string", description = "Alias of query (regex when regex=true)" },
            regex = new { type = "boolean", description = "find/replace_all: Use Regular Expressions (VS toggle)" },
            ignore_case = new { type = "boolean" },
            glob = new { type = "string", description = "find scope=project|external: rg --glob (e.g. *.cs); required for volume-root external" },
            max = new { type = "integer", description = "find scope=project: hit cap" },
            peek = new { type = "boolean", description = "find scope=project: auto open+peek top hit (default true)" },
            clear = new { type = "boolean", description = "clipboard: true = clear (all, or frame= one)" },
            frame = new { type = "string", description = "paste|put|clipboard: frame id cN (omit = current MRU)" },
            place = new { type = "string", description = "edit_op=anchor|paste|put: before|after|replace (anchor default replace). paste/put also sniper. CRITICAL: place=before/after inserts — does not overwrite locus." },
            sniper = new { type = "boolean", description = "paste|put: apply into edit sniper hold" },
            preserve = new { type = "boolean", description = "paste|put: keep frame after use (default true); false = burn" },
            body = new { type = "string", description = "put: alias of text= draft body" },
            content = new { type = "string", description = "put: alias of text=" },
            ext = new { type = "string", description = "scratch: file extension (default cs)" },
            check = new { type = "boolean", description = "take: default true — run available verify" },
            vision = new { type = "boolean", description = "take: opt-in ImageContent for agent (default false; use preview_path)" },
            see = new { type = "boolean", description = "take: alias of vision=" }
        },
        required = new[] { "op" }
    }),
    Meta("cdp_editor_scene", "Editor scene [A]: default pulse = desk go=editor snap (counts). detail=full|map or path=/locus=/doc_id= → full map+disk probe+context. Prefer before multi-step edits; single edit still cdp_buffer.", new
    {
        type = "object",
        properties = new
        {
            detail = new { type = "string", description = "pulse (default, desk-parity) | full|map (loci+disk probe)" },
            path = new { type = "string", description = "Focus file (opens context window if buffer open; forces full)" },
            doc_id = new { type = "string" },
            locus = new { type = "string", description = "buffer:doc-N from loci[]" },
            focus = new { type = "string", description = "Alias of locus" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" },
            context_lines = new { type = "integer", description = "Max lines in context window (default 80)" }
        }
    }),
    Meta("cdp_edit_plan", "Logical edit plan. YAML preferred. Mutate: steps. Fix (Roslyn code action, document): path + fix:[IDE0005,…]. sketch=fix drafts suggested_yaml from diags. Stable diagnostic ids. Routes mutate via cdp_buffer; fix via roslyn_apply_code_action.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "draft|validate|apply (preview→validate); default draft" },
            sketch = new { type = "string", description = "draft: fix|diags — build suggested_yaml from document diagnostics" },
            include = new { type = "array", items = new { type = "string" }, description = "draft: filter candidates by path/doc_id; cold paths listed too" },
            path = new { type = "string", description = "draft sketch=fix: focus file" },
            yaml = new { type = "string", description = "Preferred: YAML list of slices (path+fix and/or steps). Alias: slices_yaml=|plan=." },
            slices_yaml = new { type = "string", description = "Alias of yaml=" },
            plan = new { type = "string", description = "Alias of yaml=" },
            slices = new
            {
                description = "JSON array [{path,fix,message,steps…}] or YAML/JSON string (prefer yaml= instead)"
            },
            resolve_anchors = new { type = "boolean", description = "validate: dry-resolve anchor wires (default true)" },
            stop_on_error = new { type = "boolean", description = "apply: stop first failing step (default true)" },
            diagnose = new { type = "boolean", description = "apply mutate: per-step diagnostics (default true)" },
            flush = new { type = "boolean", description = "apply mutate: Instant Save per step (default true)" },
            skip_validate = new { type = "boolean", description = "apply: skip pre-validate (default false)" }
        }
    }),
    Meta("cdp_edit_sniper", "Edit sniper process: sight→lock→arm→fire. scope=lock (full-line + auto-peek → phase=armed). put/paste sniper hard-blocked until armed. Prefer [F:;M:;K:]/X:; [F:;T:needle] content (survives L-drift); L:=line_literal. Prefer go=scope/target on cdp_cockpit.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scope|target|clear|status (default status)" },
            from = new { type = "string", description = "scope: Select.From anchor wire [F:;M:;T:/L:]" },
            till = new { type = "string", description = "scope: Till wire, or body|enclosing" },
            max = new { type = "integer", description = "target: max nodes (default 48)" }
        }
    }),
    Meta("cdp_debug", "Debug plane (breakpoints + DAP): op=scene|bp_add|bp_remove|bp_set|bp_list|bp_clear|launch|attach|continue|stop|stop_context|step_*|stack|variables. Session defaults after cdp_open — no hand-written breakpoints JSON.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|bp_add|bp_remove|bp_set|bp_list|bp_clear|launch|attach|continue|stop|stop_context|step_over|step_into|step_out|stack|variables" },
            path = new { type = "string", description = "Source file for bp_add/bp_remove" },
            file_path = new { type = "string", description = "Alias of path" },
            line = new { type = "integer", description = "1-based line for bp_add/bp_remove" },
            condition = new { type = "string" },
            breakpoints = new
            {
                type = "array",
                description = "bp_set only: [{path|file_path,line,condition?}]",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        file_path = new { type = "string" },
                        line = new { type = "integer" },
                        condition = new { type = "string" }
                    }
                }
            },
            workspace_path = new { type = "string", description = "Optional; default = session project root after cdp_open" },
            target_path = new { type = "string", description = "Optional; default = session .csproj/.sln after cdp_open" },
            process_id = new { type = "integer", description = "attach" },
            frame_index = new { type = "integer", description = "stop_context / variables" },
            fast = new { type = "boolean" },
            configuration = new { type = "string" },
            additional_arguments = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "op" }
    }),
    ];
}
