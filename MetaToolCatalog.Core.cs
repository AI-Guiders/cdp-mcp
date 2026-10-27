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
    Meta("cdp_health", "[A] Backend health + runtime. Default detail=pulse (no LSP resolved_probe). detail=full|lsp for fat card + path resolve. Optional explain_tool=.", new
    {
        type = "object",
        properties = new
        {
            detail = new { type = "string", description = "pulse|slim (default, A) | full|lsp (C — resolved_probe per preset)" },
            explain_tool = new { type = "string", description = "Prefixed tool name to explain visibility." }
        }
    }),
    Meta("cdp_capabilities", "Mounted domains + layers.memory facets/roots + affordance counts.", new { type = "object", properties = new { } }),
    Meta("cdp_canon_stack", "Writing canon route stack (CDP-ADR-0207): embedded defaults + .cdp/project.toml overlay + personal/project paths. Requires cdp_open scm_root.", new { type = "object", properties = new { } }),
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
    Meta("cdp_open", "Open a project path: detect .sln/.csproj/tsconfig → session root+language+scm_root; embeds canon_stack (CDP-ADR-0207) for re-anchor after compact. list_changed. After open, git_*/codebase_index_*/memory_*/build_* may omit workspace_path/solution_path (session defaults). Prefer before go_to_definition. Omit path to reopen Recent[0]; or recent_index=N. Autosaves desk bookmark for cdp_restore.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "File or directory (.sln, .csproj, tsconfig.json, source file, or folder). Optional if recent_index set or Recent non-empty." },
            recent_index = new { type = "integer", description = "Optional 0-based Open Recent index (0 = last opened)." }
        }
    }, new
    {
        type = "object",
        properties = new
        {
            root = new { type = "string", description = "Session project root (absolute)." },
            kind = new { type = "string", description = "Detected project kind (sln/csproj/folder/tsconfig…)." },
            language = new { type = new[] { "string", "null" }, description = "Detected language id; null for plain folder." },
            anchors = new { type = "array", items = new { type = "string" }, description = "Detected anchor paths (.sln/.csproj/tsconfig.json)." },
            solution_or_project_path = new { type = new[] { "string", "null" }, description = "Session solution/project anchor." },
            tsconfig_path = new { type = new[] { "string", "null" } },
            scm_root = new { type = new[] { "string", "null" }, description = "Git root resolved from project root." },
            scm_risk = new { type = new[] { "string", "null" }, description = "'ancestor' when scm root sits above the project root." },
            scm_note = new { type = new[] { "string", "null" } },
            buffers_parked = new { type = "integer", description = "Clean buffers closed on open." },
            buffers_kept_dirty = new { type = "integer" },
            buffers_kept_dirty_paths = new { type = new[] { "array", "null" }, items = new { type = "string" } },
            buffer_note = new { type = new[] { "string", "null" } },
            session_phase = new { type = "string", description = "Phase after open (explore)." },
            session_object = new { type = "string", description = "Object kind after open (code)." },
            recent_count = new { type = "integer" },
            recent_store = new { type = "string", description = "Open-recent store location." },
            lsp_preset = new { type = new[] { "string", "null" }, description = "LSP pool preset id for the language (null when none)." },
            canon_lang = new { type = new[] { "string", "null" }, description = "Writing-canon effective language (ADR-0207)." },
            canon_lang_source = new { type = new[] { "string", "null" } },
            canon_stack = new { type = new[] { "object", "null" }, description = "Canon route map (lang → stack) for re-anchor after compact (ADR-0207)." },
            add_root = new { type = "boolean", description = "add_root=true call: extra root attached, primary unchanged." },
            duplicate = new { type = "boolean", description = "add_root=true: root already in session." },
            primary = new { type = "string", description = "add_root=true: unchanged primary root." },
            extra_roots = new { type = "array", items = new { type = "string" }, description = "add_root=true: all session roots." },
            note = new { type = "string" },
            list_changed = new { type = "boolean", description = "Tool catalog shortlist refreshed after open (capabilitiesRev bump rides separately)." }
        },
        required = new[] { "root", "kind" }
    }),
    Meta("cdp_buffer", "File buffer plane: op=scene|open|create|put|take|share|read|edit|diagnostics|close|reload|keep_disk|disk_peek + comfort undo|redo|history|copy|cut|paste|clipboard|find|…. put= dump draft; share with=operator|self (inbox/shelf + thin chat); share from=self|latest (pull shelf body into tool result); take= file span into agent (rare). Instant Save. Anchors: edit_op=anchor + place=before|after|into|end|replace (default replace). Relative path= → ProjectRoot.", new
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
            force = new { type = "boolean", description = "diagnostics: recompute even if version unchanged; edit set_text on existing: escape ADX-HX-001 soft-refuse; edit flush: overwrite when material disk drift (else refuse → reload|keep_disk|force)" },
            scope = new { type = "string", description = "diagnostics: syntax|project|solution (default project after cdp_open when path under open root, not .cdp/scratch); find: buffer|project|files|external (default buffer)" },
            overwrite = new { type = "boolean", description = "create: allow replace existing file" },
            allow_shrink = new { type = "boolean", description = "edit set_text: required when new body is shorter than on-disk file" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" },
            edit_op = new { type = "string", description = "edit: anchor|set_text|replace|replace_range — prefer anchor" },
            anchor = new { type = "string", description = "edit_op=anchor / copy|cut|paste: [Kind:CodeEdit; File:…; Member:…|Element:…|Line:…]" },
            at = new { type = "string", description = "Alias of anchor" },
            text = new { type = "string", description = "edit set_text / create body / replace|replace_range body / anchor text (place=before|after|into|end=insert) / paste override / find query alias" },
            old_string = new { type = "string" },
            new_string = new { type = "string", description = "replace body (preferred); also alias of text for replace|anchor|replace_range — missing both refuses (no silent delete)" },
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
            place = new { type = "string", description = "edit_op=anchor|paste|put: before|after|into|end|replace (anchor default replace). csharp M: before|after = sibling outside member; into|end = inside braces; type/ns before|after = inside type. paste/put also sniper. CRITICAL: place=before/after/into/end inserts — does not overwrite locus." },
            sniper = new { type = "boolean", description = "paste|put: apply into edit sniper hold" },
            preserve = new { type = "boolean", description = "paste|put: keep frame after use (default true); false = burn" },
            body = new { type = "string", description = "create|put: alias of text= body (create: prefer text=)" },
            content = new { type = "string", description = "create|put: alias of text= (create: prefer text=)" },
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
    }, new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["schema"] = new { type = "string", description = "edit_plan/v0." },
            ["op"] = new { type = "string", description = "draft | validate | apply (echo of request op)." },
            ["ok"] = new { type = "boolean" },
            ["sketch"] = new { type = new[] { "string", "null" }, description = "draft: echo of sketch=." },
            ["session"] = new { type = "object", description = "draft: { project_root, language }." },
            ["candidates"] = new
            {
                type = "array",
                items = new Dictionary<string, object?> { ["$ref"] = "#/$defs/Candidate" },
                description = "draft: per-file candidates (open buffers + cold include paths)."
            },
            ["candidate_count"] = new { type = "integer", description = "draft." },
            ["suggested_yaml"] = new { type = new[] { "string", "null" }, description = "draft sketch=fix: fix-YAML drafted from diagnostics." },
            ["example_yaml"] = new { type = "string", description = "draft: mutate steps template." },
            ["example_fix_yaml"] = new { type = "string", description = "draft: fix template." },
            ["slice_count"] = new { type = "integer", description = "validate/apply." },
            ["slices"] = new
            {
                type = "array",
                items = new Dictionary<string, object?> { ["$ref"] = "#/$defs/SliceResult" },
                description = "validate/apply: per-slice verdicts."
            },
            ["stop_on_error"] = new { type = "boolean", description = "apply: echo." },
            ["thrash"] = new { type = new[] { "string", "null" }, description = "apply: thrash latch pulse line (null cold)." },
            ["thrash_hot"] = new { type = "boolean", description = "apply: thrash latch hot — prefer anchor edits." },
            ["next"] = new { type = new[] { "string", "null" }, description = "apply: suggested next call." },
            ["error"] = new { type = new[] { "string", "null" }, description = "apply: validate_failed when pre-validation blocked the run." },
            ["validate"] = new { type = new[] { "object", "null" }, description = "apply: embedded validate report on validate_failed." },
            ["hint"] = new { type = "string" }
        },
        ["required"] = new[] { "schema", "op", "ok" },
        ["$defs"] = new Dictionary<string, object?>
        {
            ["Candidate"] = new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "File path." },
                    doc_id = new { type = new[] { "string", "null" }, description = "Open buffer id (open-buffer candidates)." },
                    dirty = new { type = new[] { "boolean", "null" } },
                    language = new { type = new[] { "string", "null" } },
                    version = new { type = new[] { "integer", "null" } },
                    line_count = new { type = new[] { "integer", "null" } },
                    opened = new { type = new[] { "boolean", "null" }, description = "false: cold path — cdp_buffer op=open before apply." },
                    thrash_risk = new { type = new[] { "boolean", "null" }, description = "Line count at thrash threshold — avoid whole-file set_text." },
                    preferred_edit_op = new { type = new[] { "string", "null" }, description = "anchor (csharp/xml) | replace." },
                    attractor = new { type = new[] { "string", "null" }, description = "Pointer to edit_plan|sniper when thrash risk." },
                    ok = new { type = new[] { "boolean", "null" }, description = "false: unresolvable include path." },
                    error = new { type = new[] { "string", "null" }, description = "path_not_found / resolve failure." },
                    hint = new { type = new[] { "string", "null" } }
                }
            },
            ["FixHit"] = new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "Stable diagnostic id (IDE0005…)." },
                    line = new { type = "integer" },
                    column = new { type = "integer" },
                    message = new { type = "string" }
                },
                required = new[] { "id", "line", "column", "message" }
            },
            ["FixCheck"] = new
            {
                type = "object",
                properties = new
                {
                    ok = new { type = "boolean" },
                    path = new { type = new[] { "string", "null" }, description = "validate: target file." },
                    ids = new { type = new[] { "array", "null" }, items = new { type = "string" }, description = "validate: requested fix ids." },
                    hits = new
                    {
                        type = new[] { "array", "null" },
                        items = new Dictionary<string, object?> { ["$ref"] = "#/$defs/FixHit" },
                        description = "validate: located diagnostic hits."
                    },
                    applied = new { description = "apply: fix-apply detail." },
                    error = new { type = new[] { "string", "null" } }
                },
                required = new[] { "ok" }
            },
            ["StepResult"] = new
            {
                type = "object",
                properties = new
                {
                    index = new { type = "integer" },
                    path = new { type = new[] { "string", "null" } },
                    edit_op = new { type = new[] { "string", "null" }, description = "replace | anchor | …" },
                    ok = new { type = "boolean" },
                    errors = new { type = new[] { "array", "null" }, items = new { type = "string" }, description = "validate: per-step errors." },
                    result = new { description = "apply: underlying cdp_buffer step result." }
                },
                required = new[] { "index", "ok" }
            },
            ["SliceResult"] = new
            {
                type = "object",
                properties = new
                {
                    message = new { type = new[] { "string", "null" }, description = "Why this logical group." },
                    path = new { type = new[] { "string", "null" }, description = "fix slice target file." },
                    ok = new { type = "boolean" },
                    error = new { type = new[] { "string", "null" } },
                    fix = new
                    {
                        type = new[] { "object", "null" },
                        oneOf = new[] { new Dictionary<string, object?> { ["$ref"] = "#/$defs/FixCheck" } },
                        description = "fix-slice check/apply result."
                    },
                    step_count = new { type = new[] { "integer", "null" }, description = "validate." },
                    errors = new { type = new[] { "array", "null" }, items = new { type = "string" }, description = "validate: slice errors (step[i]:… prefixed)." },
                    steps = new
                    {
                        type = new[] { "array", "null" },
                        items = new Dictionary<string, object?> { ["$ref"] = "#/$defs/StepResult" },
                        description = "validate/apply: per-step records."
                    }
                },
                required = new[] { "ok" }
            }
        }
    }),
    Meta("cdp_peel", "FileLines peel — thin Meta over live Roslyn move_members_to_partial_file. path= + members= + out=; apply=false preview (default), true writes + DependentUpon. Session injects solution after cdp_open. Alias go=peel. Prefer after go=refactor partials.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Source .cs (type locus); alias file_path=" },
            file_path = new { type = "string", description = "Alias of path=" },
            members = new { type = "array", items = new { type = "string" }, description = "Member names to move; alias member_names=. CSV string also ok." },
            member_names = new { type = "array", items = new { type = "string" }, description = "Alias of members=" },
            @out = new { type = "string", description = "New partial .cs path; alias output_file_path=" },
            output_file_path = new { type = "string", description = "Alias of out=" },
            line = new { type = "integer", description = "1-based cursor on type (default 1)" },
            column = new { type = "integer", description = "1-based column (default 1)" },
            apply = new { type = "boolean", description = "false=preview (default); true=TryApplyChanges" },
            add_dependent_upon = new { type = "boolean", description = "After apply: DependentUpon in csproj (default true)" },
            solution_or_project_path = new { type = "string", description = "Optional; default = session after cdp_open" }
        }
    }),
    Meta("cdp_edit_sniper", "Edit sniper process: sight→lock→arm→fire. scope=lock (full-line + auto-peek → phase=armed). put/paste sniper hard-blocked until armed. Prefer [Kind:CodeEdit; File:…; Member:…|Text:…|Line:…]. Line: = line_literal corridor. Prefer go=scope/target on cdp_cockpit.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scope|target|clear|status (default status)" },
            from = new { type = "string", description = "scope: Select.From [Kind:CodeEdit; File:…; Member:…|Line:…|Text:…]" },
            till = new { type = "string", description = "scope: Till wire, or body|enclosing" },
            max = new { type = "integer", description = "target: max nodes (default 48)" }
        }
    }, new
    {
        type = "object",
        properties = new
        {
            schema = new { type = "string", description = "edit_sniper/v0." },
            ok = new { type = "boolean" },
            op = new { type = "string", description = "scope | target | peek | aim | clear | status." },
            phase = new { type = "string", description = "armed after scope lock — fire (put/paste sniper) hard-gated until then." },
            process = new { type = "string", description = "scope: sight→lock→arm." },
            hold = ToolSchemas.NullableFor<EditSniper.Corridor>(),
            resolve = new { type = "string", description = "scope: anchor resolution detail." },
            count = new { type = "integer", description = "scope/status: corridor lines | target: nodes found." },
            start_line = new { type = "integer", description = "scope." },
            end_line = new { type = "integer", description = "scope." },
            text = new { type = new[] { "string", "null" }, description = "scope/status: corridor peek text." },
            truncated = new { type = "boolean", description = "target: max= cap hit." },
            targets = new
            {
                type = "array",
                items = ToolSchemas.For<EditSniper.SniperTarget>(),
                description = "target: in-corridor nodes."
            },
            next = new { type = "array", items = new { type = "object" }, description = "Suggested next go= steps." },
            error = new { type = "string", description = "from_required | no_scope | unknown_op | wire resolve errors (ok=false)." },
            hint = new { type = "string" }
        },
        required = new[] { "schema", "ok" }
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
