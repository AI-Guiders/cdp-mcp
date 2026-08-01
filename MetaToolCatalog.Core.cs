#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog peeled from Program top-level (soft-warn).</summary>
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
    Meta("cdp_recent", "List Open Recent projects/solutions (agent mirror of classic IDE + CIDE anchor→solution history).", new
    {
        type = "object",
        properties = new
        {
            take = new { type = "integer", description = "Max entries (default 12)." }
        }
    }),
    Meta("cdp_restore", "Restore Previous desk after MCP kill/reload (dual-instance comfort). Reopens last project + buffer paths from disk bookmark (%LocalAppData%/cdp-mcp/desk-previous.json). Autosaved on cdp_open / buffer open. NOT full LLM chat context. op=peek|restore (default restore). Alias cockpit go=restore. Cold tools also auto-warm once/process.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "restore (default) | peek" }
        }
    }),
    Meta("cdp_deploy", "Dual-instance Deploy — runs publish-and-deploy.ps1. Hard defaults to sibling install (D:\\cdp-mcp ↔ D:\\cdp-mcp-debug) so KillRunning does not target self. Soft stages .next. mode=rollout: soft sibling→soft self→hard sibling + hard_self.argv for terminal_*. Crystal: switch seat → go=deploy (desk auto-warms). dry_run= to preview. Alias go=deploy.", new
    {
        type = "object",
        properties = new
        {
            mode = new { type = "string", description = "soft|hard|rollout (default hard; rollout=soft sibling→soft self→hard sibling)" },
            target = new { type = "string", description = "sibling|self|release|debug|path (default sibling)" },
            force = new { type = "boolean", description = "allow hard deploy onto self install (escape)" },
            dry_run = new { type = "boolean", description = "resolve policy only — no powershell" },
            script = new { type = "string", description = "optional path to publish-and-deploy.ps1" },
            use_nuget = new { type = "boolean", description = "pass -UseNuGet to aid-publish" },
            no_nudge = new { type = "boolean", description = "skip CDP_RELOAD_NUDGE bump" },
            include_raw = new { type = "boolean", description = "include stdout_tail/stderr_tail (default slim pulse+locus)" },
            include_raw_output = new { type = "boolean", description = "alias of include_raw" }
        }
    }),
    Meta("cdp_elicit", "Spike: MCP elicitation/create → host UI (path 2). op=peek (client caps) | ask (form Да/Нет/Обсудить). Proves whether Cursor advertises elicitation.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "peek|ask (default ask)" },
            message = new { type = "string", description = "ask: prompt shown to operator" },
            ask = new { type = "string", description = "alias of message" }
        }
    }),
    Meta("cdp_land", "Land via Family:navigation Anchor wire (ADR 0186). NOT Deep-Link/URI. Pass anchor=[Family:navigation;Command:open|goto|restore|show|go;…]. Nested Anchor:[…] reuses code/xml resolve. Alias go=land.", new
    {
        type = "object",
        properties = new
        {
            anchor = new { type = "string", description = "[Family:navigation;Command:…;Go:…;Anchor:[…]]" },
            at = new { type = "string", description = "Alias of anchor" },
            wire = new { type = "string", description = "Alias of anchor" }
        },
        required = new[] { "anchor" }
    }),
    Meta("cdp_cide_presentation", "Operator CIDE glass wire (instant). op=scene|get|set. set topology=(P)(F)(M) and/or tier=cockpit|compact|auto and/or pfd_primary=/mfd_primary= and/or mfd_page=SolutionExplorer → presentation-LATEST latch → CIDE live apply. Not agent cdp_settings desk; does not mutate repo workspace.toml. Alias go=cide_presentation.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|get|set (default scene)" },
            topology = new { type = "string", description = "set: display.screens.topology e.g. (P)(F)(M)" },
            value = new { type = "string", description = "alias of topology" },
            presentation = new { type = "string", description = "alias of topology" },
            tier = new { type = "string", description = "set: display.presentation.tier auto|compact|cockpit" },
            pfd_primary = new { type = "string", description = "set: display.instruments.pfd_primary e.g. workspace_map|solution_explorer_tree" },
            mfd_primary = new { type = "string", description = "set: display.instruments.mfd_primary" },
            pfd_status_strip = new { type = "string", description = "set: display.instruments.pfd_status_strip" },
            forward_status_strip = new { type = "string", description = "set: display.instruments.forward_status_strip" },
            instruments = new { type = "string", description = "set: JSON object of instrument slot→id (merged with pfd_primary/…)" },
            mfd_page = new { type = "string", description = "set: MfdShellPage name e.g. SolutionExplorer|Chat|Terminal" },
            page = new { type = "string", description = "alias of mfd_page" }
        }
    }),
    Meta("cdp_intercom", "Dual-cockpit Intercom voice @PF/@PM. op=scene|send|ack|history|presence. send to=pm body= → intercom-LATEST + journal. presence seat= state=idle|composing|busy → intercom-presence-LATEST (partner observability; no thinking dump). Virtual History: op=history. Alias go=intercom.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|get|inbox|send|ack|history|presence (default scene)" },
            to = new { type = "string", description = "send: pm|pf or @PM|@PF (default pm)" },
            from = new { type = "string", description = "send: optional seat override (default pf); presence: alias of seat" },
            body = new { type = "string", description = "send: message text" },
            message = new { type = "string", description = "send: alias of body" },
            text = new { type = "string", description = "send: alias of body" },
            id = new { type = "string", description = "ack: optional message id" },
            limit = new { type = "integer", description = "history: last N messages (default 20, max 200)" },
            seat = new { type = "string", description = "presence: pf|pm (default pf)" },
            state = new { type = "string", description = "presence: idle|composing|busy" },
            status = new { type = "string", description = "presence: alias of state" },
            ttl_s = new { type = "integer", description = "presence: optional TTL seconds (composing/busy stale after)" }
        }
    }),
    Meta("cdp_citizen", "Citizen completions host (ADR-0028). op=scene|keys|turn. turn message= [board=] [dry_run=true] [model=] — persona + wire inject; OpenAI-compat (Cloud.ru FM via open_ai_*) or Anthropic via ai-keys.toml. Alias go=citizen.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|keys|turn (default scene)" },
            message = new { type = "string", description = "turn: user text" },
            body = new { type = "string", description = "turn: alias of message" },
            board = new { type = "string", description = "turn: optional desk board lines (newline-separated seat rows)" },
            sa = new { type = "string", description = "turn: optional sa field" },
            peer = new { type = "string", description = "turn: optional peer field" },
            next = new { type = "string", description = "turn: optional next field" },
            tm = new { type = "string", description = "turn: optional tm field" },
            model = new { type = "string", description = "turn: model id (default: Cloud.ru FM or Anthropic sonnet by provider)" },
            dry_run = new { type = "boolean", description = "turn: build messages only, no provider call" },
            inject = new { type = "boolean", description = "turn: prepend wire afferent (default true)" }
        }
    }),
    Meta("cdp_mcp", "Agent MCP outlet (ADR 0187) — Cursor-parity control inside CDP. op=scene|presets|mount|tools|call|unmount. Mount guests (Serena/memory/…) for a task; child tools NEVER enter host ListTools. Alias go=mcp_scene|mcp_mount|…", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene (default) | presets | mount | tools | call | unmount" },
            id = new { type = "string", description = "Mount id (default=preset name)" },
            server = new { type = "string", description = "Mounted server id for tools/call/unmount" },
            preset = new { type = "string", description = "mount: memory|serena|filesystem|time|…" },
            command = new { type = "string", description = "mount: exe if not preset" },
            args = new { description = "mount: string[] argv; call: object of child tool args" },
            tool = new { type = "string", description = "call: child tool name" },
            name = new { type = "string", description = "call alias of tool; mount transport name" },
            filter = new { type = "string", description = "tools: name/description filter" },
            take = new { type = "integer", description = "tools: max (default 40)" }
        }
    }),
    Meta("cdp_browser", "Agent internet browser in CDP (ADR 0188) — lynx + Chromium UA spoof. NOT Cursor Browser. op=scene|which|open|search|dump|links|follow|back|forward|close. Search default=DDG HTML. Alias go=scene_internet_browser.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene (default) | which | open | search | dump | links | follow | back | forward | close" },
            url = new { type = "string", description = "open: https://… (http/file ok; bare host → https://)" },
            q = new { type = "string", description = "search: query text" },
            query = new { type = "string", description = "search: alias of q" },
            engine = new { type = "string", description = "search: ddg (default) | google | bing" },
            tab = new { type = "string", description = "Browser tab id (default main / active; search→search)" },
            link = new { type = "integer", description = "follow: N from op=links" },
            filter = new { type = "string", description = "links: filter urls" },
            take = new { type = "integer", description = "links: max" },
            width = new { type = "integer", description = "lynx -width (default 100)" },
            max_chars = new { type = "integer", description = "cap dump body" },
            timeout_seconds = new { type = "integer", description = "fetch timeout (default 45)" },
            useragent = new { type = "string", description = "override UA (default Chromium spoof; env CDP_BROWSER_UA)" }
        }
    }),
    Meta("cdp_settings", "Agent IDE Tools→Options (ADR 0190). op=options|page|get|set|lsp_probe|lsp_install|lsp_ensure|lsp_add. page=languages → install LSP via IDE shell. Alias go=options / lsp_ensure.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "options|page|catalog|get|set|unset|lsp_probe|lsp_install|lsp_ensure|lsp_add|which" },
            page = new { type = "string", description = "languages|internet|desk|shell|mcp|environment|process" },
            section = new { type = "string", description = "alias of page" },
            key = new { type = "string", description = "get/set: browser.search_engine | desk.default_layout | …" },
            value = new { type = "string", description = "set value" },
            id = new { type = "string", description = "lsp_*: python|go|rust|yaml|json|markdown" },
            language = new { type = "string", description = "alias of id" },
            via = new { type = "string", description = "lsp_install/ensure: npm|pip|pipx|go|rustup|scoop|winget" },
            command = new { type = "string", description = "lsp_add: executable name" },
            args = new { description = "lsp_add: string[] server args (default --stdio)" },
            writable_only = new { type = "boolean", description = "catalog: only hot user keys" }
        }
    }),
    Meta("cdp_search", "Agent-native search organ (ADR-0009). Prefer over shell/Cursor Grep. Axes: what=text|index|symbol, where=buffer|project|external|dirty|buffers (+roots[]/path=), shape=slim|list|raw. op=run|refine|last|clear. Alias go=find_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "run|refine|last|clear (default run)" },
            what = new { type = "string", description = "text (default) | index | symbol" },
            where = new { type = "string", description = "project|external|dirty|buffers|buffer" },
            shape = new { type = "string", description = "slim (default) | list | raw" },
            query = new { type = "string", description = "needle (aliases text= pattern= q=)" },
            path = new { type = "string", description = "subdir or absolute (external requires rooted)" },
            roots = new { description = "string[] multi-root / file list" },
            exclude = new { description = "refine: string[] path substrings to drop" },
            glob = new { type = "string", description = "rg --glob" },
            regex = new { type = "boolean" },
            ignore_case = new { type = "boolean" },
            max = new { type = "integer" },
            peek = new { type = "boolean", description = "auto land top hit (default true)" },
            only_dirty = new { type = "boolean", description = "where=buffers: only Dirty buffers" }
        }
    }),
    Meta("cdp_sa", "Agent-native code SA before refactor (ADR-0010). Fuse gates+dirty+clones → verdict leave|touch|split|need_more. Axes: locus/path/line, scope=file|buffer|dirty|project, depth=pulse|slim|full. Alias go=sa_desk (NOT go=sa EICAS).", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "file locus (opens buffer for gates)" },
            locus = new { type = "string", description = "alias of path= or seat locus id" },
            anchor = new { type = "string", description = "[F:;L:;C:] wire" },
            line = new { type = "integer", description = "for find_usages next" },
            column = new { type = "integer" },
            scope = new { type = "string", description = "file|buffer|dirty|project" },
            depth = new { type = "string", description = "pulse|slim (default)|full" }
        }
    }),
    ];
}
