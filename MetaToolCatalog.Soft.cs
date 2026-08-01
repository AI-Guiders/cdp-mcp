#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog peeled from Program top-level (soft-warn).</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> SoftOrgans() =>
    [
    Meta("cdp_refactor", "Refactor decide desk — debt map + before/after budget + blast next + partials seam. op=plan|debt|budget|blast|partials|pulse. After sa_desk; Alias go=refactor_plan.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "plan (default)|debt|budget|blast|partials|pulse" },
            path = new { type = "string", description = "file locus" },
            scope = new { type = "string", description = "file|buffer|project" },
            line = new { type = "integer", description = "blast: find_usages line" },
            column = new { type = "integer" },
            topic = new { type = "string", description = "partials: suggested TypeName.Topic.cs" },
            after_lines = new { type = "integer", description = "budget what-if file lines" },
            extract_lines = new { type = "integer", description = "budget: before - N" },
            after_method_lines = new { type = "integer", description = "budget what-if worst method" }
        }
    }),
    Meta("cdp_debug_sa", "Agent-native Debug-SA (ADR-0011). Fuse DAP+bp+launch → verdict idle|continue|step|fix_bp|stop_rebuild|attach|need_more. Axes: scope=session|bp|stop, depth=pulse|slim|full. Alias go=debug_desk (NOT go=debug raw scene; NOT go=sa EICAS).", new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string", description = "session (default) | bp | stop" },
            depth = new { type = "string", description = "pulse|slim (default)|full" }
        }
    }),
    Meta("cdp_test_sa", "Agent-native Test-SA (ADR-0012). Fuse last_run → verdict need_more|discover|run|retest|green. Axes: scope=session|failed|last, depth=pulse|slim|full. Alias go=test_desk (NOT go=test/test_scene raw).", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "optional .sln/.csproj override" },
            scope = new { type = "string", description = "session (default) | failed | last" },
            depth = new { type = "string", description = "pulse|slim (default)|full" }
        }
    }),
    Meta("cdp_build_sa", "Agent-native Build-Ship-SA (ADR-0013). Fuse DAP+dirty+ahead → verdict need_more|stop_rebuild|build|preflight|ship|push|clean. Axes: scope=session|build|ship, depth=pulse|slim|full. Alias go=build_desk (NOT go=build actuator; NOT go=ship take).", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "optional .sln/.csproj override" },
            scope = new { type = "string", description = "session (default) | build | ship" },
            depth = new { type = "string", description = "pulse|slim (default)|full" }
        }
    }),
    Meta("cdp_crm", "CRM callout panel (ADR-0014). Closed codes: approved|stabilized|go_around|hold|unable|negative|say_again|continue|roger|wilco. op=scene|call|respond|last|clear|lexicon. Alias go=crm. Operator act → SSOT; agent reads pulse — no reject essays in chat.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|call|respond|last|clear|lexicon" },
            code = new { type = "string", description = "respond: approved|stabilized|go_around|hold|…" },
            ask = new { type = "string", description = "call: what operator should answer" },
            kind = new { type = "string", description = "call: general|plan|…" },
            ref_id = new { type = "string", description = "call: correlation id" },
            why = new { type = "string", description = "respond: short code ≤80 chars, not essay" }
        }
    }),
    Meta("cdp_arch", "Architecture staging board (ADR 0196) — ontological kneeboard, not Miro. Roles CCU|Channel|CDS|Compositor|Surface + candidates as CodeAnchor wires [F:;M:;K:]. op=scene|add_role|add_candidates|elect|reject|edge|promote|clear|roles|as_built. Alias go=arch_desk|board. Board ≠ code until promote (plan-only v0).",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|add_role|add_candidates|elect|reject|edge|promote|clear|roles|as_built" },
                role = new { type = "string", description = "ccu|channel|cds|ids|compositor|surface|instrument|databus|dal|transport" },
                role_id = new { type = "string", description = "optional stable id for the role slot" },
                id = new { type = "string", description = "alias of role_id" },
                anchors = new { type = "string", description = "CodeAnchor wires [F:;M:;K:] (array or comma-separated); not bare paths" },
                candidates = new { type = "string", description = "alias of anchors" },
                candidate = new { type = "string", description = "elect/reject: candidate id|label|wire" },
                from = new { type = "string", description = "edge: from role id|kind" },
                to = new { type = "string", description = "edge: to role id|kind" },
                kind = new { type = "string", description = "edge: feeds|mounts|projects|wires; or alias of role on add_role" },
                note = new { type = "string", description = "optional why on role" },
                view = new { type = "string", description = "scene: plan|as_built" },
                profile = new { type = "string", description = "as_built: cide|cdp_desk|unknown — overrides auto-detect" }
            }
        }),
    Meta("cdp_onboard", "Cold-start explore/onboard desk — no ADR required. Scans open ProjectRoot for entrypoints, top folders, verticals, docs presence + next[]. Not a VS Code Map. op=scene|scan|clear. Alias go=onboard_desk|explore_desk. layout=onboard|explore → M seat.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|scan|clear" }
            }
        }),
    Meta("cdp_toolchain", "Toolchain ensure (ADR 0198) — runtime/compiler/SDK on PATH; DAL-adjacent; NOT lsp_ensure. Any id: python|gcc|javac|go|+custom. op=scene|probe|ensure|install|add|which. Alias go=toolchain|toolchain_ensure.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|probe|ensure|install|add|which" },
                id = new { type = "string", description = "python|gcc|javac|go|custom" },
                via = new { type = "string", description = "winget|scoop|..." },
                bins = new { type = "string", description = "add: comma bins" },
                pairs_lsp = new { type = "string", description = "optional lsp id after ensure" }
            }
        }),
    Meta("cdp_md_author", "Markdown authoring INCLUDE organ (CIDE ADR 0023). op=scene|check|expand|export. Syntax {{ INCLUDE: rel/path }}. scope=all (default) or fence (CIDE preview parity). Alias go=md_author.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|check|expand|export" },
                path = new { type = "string", description = "source .md (absolute or project-relative)" },
                @out = new { type = "string", description = "export: output path (default {name}.expanded.md)" },
                scope = new { type = "string", description = "all|fence" },
                max_depth = new { type = "integer", description = "INCLUDE nest limit (default 5)" },
                max_chars = new { type = "integer", description = "expand/export body cap in response" }
            }
        }),
    Meta("cdp_fdr", "Black-box FDR — dense tool-call flight tape (organ/op/latency/outcome/phase). Incident analysis, not chat dump. op=scene|tail|stats|slow|suggest|apply|clear_overlay. Alias go=fdr. VDR deferred. timeout_wake: suggest from p95/wake → apply overlay (per-call override still wins).",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|tail|stats|slow" },
                limit = new { type = "integer", description = "tail/stats/slow lookback" },
                lookback = new { type = "integer", description = "alias of limit for stats/slow" },
                min_ms = new { type = "integer", description = "slow: min elapsed ms (default 1000)" }
            }
        }),
    Meta("cdp_teeth", "Guest-host teeth organ — one-glance CDT/Stop, remount·oom delivery, OOM tooth, partner away/here. Afferent teeth-tape (not FDR). op=scene|tail|explain. Alias go=teeth. Related ADR-0027/0029.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|tail|explain" },
                limit = new { type = "integer", description = "tail lookback" },
                id = new { type = "string", description = "explain: arm id focus" },
                arm = new { type = "string", description = "alias of id" },
                cdt = new { type = "boolean", description = "scene: live CDT sample (default false)" }
            }
        }),
    Meta("cdp_postmortem", "Ethical SoftOrgan postmortem — blameless peel (happened/system_root/why_repeated/fix/do_not). Scrubs secrets; refuses blame + chat dump. op=scene|template|draft|record|list. Persist failure+finding+FDR call_id. Alias go=postmortem|pm|retro. Integrity=honesty+exit.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|template|draft|record|list" },
                happened = new { type = "string", description = "What happened (facts, no blame)" },
                system_root = new { type = "string", description = "System/mechanism root" },
                why_repeated = new { type = "string", description = "Why it repeated" },
                fix = new { type = "string", description = "Fix shipped or planned" },
                do_not = new { type = "string", description = "Anti-pattern for next agent" },
                title = new { type = "string" },
                tool = new { type = "string" },
                fdr_call_id = new { type = "string", description = "FDR call_id anchor" },
                call_id = new { type = "string", description = "alias of fdr_call_id" },
                category = new { type = "string", description = "failures category (default unknown; postmortem→unknown)" },
                fingerprint = new { type = "string" },
                task_id = new { type = "string" },
                project_id = new { type = "string" },
                workspace_path = new { type = "string" },
                limit = new { type = "integer", description = "list lookback" }
            }
        }),
    Meta("cdp_learn", "Lean dialogue learning desk — stash findings so compaction cannot eat them. op=scene|stash|list|recall|promote. Journal under ws state; promote → agent-notes work/projects/_learn (or path=). Alias go=learn. Not findings (file memos) and not TM.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|stash|list|recall|promote" },
                title = new { type = "string", description = "stash: short card title" },
                body = new { type = "string", description = "stash: concentrated learning (required)" },
                text = new { type = "string", description = "alias of body=" },
                topic = new { type = "string", description = "optional topic tag" },
                tags = new { type = "string", description = "comma/semicolon tags" },
                id = new { type = "string", description = "recall/promote: card id (or latest)" },
                path = new { type = "string", description = "promote: knowledge-relative path (default work/projects/_learn/{id}.md)" },
                limit = new { type = "integer", description = "list: max cards (default 20)" },
                primary = new { type = "string", description = "stash override; else inherit go=project_switch latch" },
                scope = new { type = "string", description = "stash override active_scope; else inherit latch" }
            }
        }),
    Meta("cdp_scope", "AN Project Switch latch on desk — PRIMARY + SCOPE. op=scene|set|recall|clear. Args primary=/scope= or text=[PRIMARY:…][SCOPE:…]. Alias go=project_switch|ps (NOT go=scope — that is EditSniper). Learn stash inherits latch.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|set|recall|clear" },
                primary = new { type = "string", description = "project-id (AN PRIMARY)" },
                scope = new { type = "string", description = "active_scope slice (AN SCOPE)" },
                active_scope = new { type = "string", description = "alias of scope=" },
                text = new { type = "string", description = "message with [PRIMARY:…] / [SCOPE:…] markers" },
                message = new { type = "string", description = "alias of text=" }
            }
        }),
    Meta("cdp_files", "Agent-native File Manager (ADR-0016). Utility — not project-bound. where=cwd|project|external (+path=). op=scene|list|cd|up|stat|tree|open|text|search|roots|clear. text= lynx-like dump (pandoc/pdftotext). shape=slim|list. Alias go=files_desk. Prefer over shell ls/dir. Search facet → find_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|list|cd|up|stat|tree|open|text|search|roots|clear" },
            where = new { type = "string", description = "cwd|project|external" },
            path = new { type = "string", description = "absolute (external) or relative to cwd" },
            name = new { type = "string", description = "cd/open/stat relative name" },
            filter = new { type = "string", description = "glob or substring" },
            kind = new { type = "string", description = "all|file|dir" },
            shape = new { type = "string", description = "slim (default)|list|raw" },
            depth = new { type = "integer", description = "tree depth 1..4" },
            query = new { type = "string", description = "search facet → find_desk" },
            hidden = new { type = "boolean", description = "include hidden entries" },
            max_chars = new { type = "integer", description = "text: dump cap (default 12000)" },
            @as = new { type = "string", description = "open: buffer|edit to force buffer for docs (default text for pdf/docx/…)" }
        }
    }),
    Meta("cdp_ignite", "AutoIgnition via Chrome DevTools (CDT) into Cursor Composer — not Cognitive CDP. Requires Cursor --remote-debugging-port=9222. op=scene|probe|chats|send|arm|disarm|list|hygiene|plateau|continuity|resume|autonomous|hild|halt|await_partner. ARM: when=build_finished|test_finished|shell_finished|human_away|timer task= (TM label only). HILD: Composer text idle 30s on Voice → human_away once → AutoI wake (default ARMED; op=hild_off). Default charge=minimal: canonical wake text + amnesia/compaction postfix at fire (no TM body in composer). charge=custom only for legacy templates. last_once=/await_partner: fire once → awaiting latch. halt=stop-world (autonomous+HILD off, clear arms, await partner). autonomous default ARMED: auto LeafPlateau does not await_partner — seed-wake instead. Alias go=ignite_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|probe|chats|send|arm|disarm|list|autonomous|autonomous_on|autonomous_off|hild|hild_on|hild_off|resume|continuity|halt|await_partner|await_operator" },
            message = new { type = "string", description = "send: optional override; arm: ignored unless charge=custom" },
            task = new { type = "string", description = "arm: Task Manager label (SSOT); not injected into composer" },
            charge = new { type = "string", description = "arm: minimal (default)|custom|legacy — minimal fires canonical+amnesia postfix" },
            when = new { type = "string", description = "arm: build_finished|test_finished|shell_finished|human_away|timer" },
            @event = new { type = "string", description = "alias of when=" },
            @in = new { type = "string", description = "arm timer: 30s|5m|2h" },
            chat = new { type = "string", description = "optional chat title substring" },
            id = new { type = "string", description = "disarm id= / arm custom id" },
            all = new { type = "boolean", description = "disarm all=true (under autonomous: except autonomy means unless force)" },
            force = new { type = "boolean", description = "disarm: clear autonomy means too; arm: override epic-closed / last_once gates" },
            last_once = new { type = "boolean", description = "arm: fire once → awaiting_partner latch" },
            armed = new { type = "boolean", description = "autonomous|hild: true|false latch (default ARMED)" },
            ok_only = new { type = "boolean", description = "arm: fire only on green build/test (default true)" },
            settle_seconds = new { type = "integer", description = "arm: delay before CDT send after event (default 8)" },
            port = new { type = "integer", description = "CDT port (default 9222)" },
            wait_seconds = new { type = "integer", description = "max wait for idle (not Stop/Queue), default 90" }
        }
    }),
    Meta("cdp_webcam", "Sense desk — in-proc Shared+OpenCv+NAudio+Whisper. op=scene|frame|burst|av|screen|audio|transcribe|ocr|analyze. av: concurrent cam+mic (capture_av_burst parity). Alias go=webcam_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|frame|burst|av|screen|audio|transcribe|ocr|analyze" },
            camera_index = new { type = "integer", description = "default 0" },
            file_name = new { type = "string", description = "output base name without extension" },
            workspace_path = new { type = "string", description = "override; default = session project root" },
            width = new { type = "integer" },
            height = new { type = "integer" },
            jpeg_quality = new { type = "integer" },
            duration_sec = new { type = "integer", description = "burst/av/screen/audio: seconds" },
            target_fps = new { type = "integer", description = "burst/av/screen: fps" },
            burst_name = new { type = "string", description = "burst/screen/av: folder/session name" },
            session_name = new { type = "string", description = "av: session folder name" },
            save_video = new { type = "boolean", description = "av: write video.mp4 (default true)" },
            output_subdir = new { type = "string", description = "relative output dir" },
            sample_rate = new { type = "integer", description = "audio/av: Hz (default 16000)" },
            channels = new { type = "integer", description = "audio/av: 1|2 (default 1)" },
            device_number = new { type = "integer", description = "audio/av: WaveIn device index" },
            audio_path = new { type = "string", description = "transcribe: wav/webm under workspace" },
            model_path = new { type = "string", description = "transcribe: ggml model; default WHISPER_MODEL_PATH" },
            language = new { type = "string", description = "transcribe: whisper language or auto" },
            max_segments = new { type = "integer", description = "transcribe: segment cap" },
            images_dir = new { type = "string", description = "ocr: folder of images" },
            file_path = new { type = "string", description = "ocr/transcribe: single file path" },
            lang = new { type = "string", description = "ocr: tesseract langs; transcribe alias of language=" },
            sample_every = new { type = "integer", description = "ocr/analyze: every N-th file" },
            max_images = new { type = "integer", description = "ocr: cap" },
            max_frames = new { type = "integer", description = "analyze: cap" },
            burst_dir = new { type = "string", description = "analyze: folder of frames" },
            scene_cut_threshold = new { type = "number", description = "analyze: motion cut threshold 0..255" },
            output_json_path = new { type = "string", description = "ocr: write JSON path under workspace" }
        }
    }),
    Meta("cdp_pressure", "L1 pre-compact prep desk. On pressure notify (~2–3 turns before host summarization): op=arm → checklist → op=stash body= (also appends memo line). Anti-compaction: op=memo body= / op=line. Recall gate (ADR-0024): op=recall → ready when SSOT (body+plan/ignite) else pull → op=reconcile → op=align → op=ready; strict=true forces pull; op=steer|ssot|fast shortcuts. Must axes: AutoIgnition re-ARM, Task Manager, CDP habitat, Domain (.cdp/domain). Alias go=pressure_desk|pressure. Does not offer export ritual to operator.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|arm|stash|memo|line|clear|disarm|recall|reconcile|steer|ssot|fast|align|ready|gate" },
            body = new { type = "string", description = "stash|memo: markdown/text — goal, decisions, open, next, ignite, plan" },
            text = new { type = "string", description = "alias of body=" },
            why = new { type = "string", description = "arm|memo: reason (default L1 pressure notify)" },
            ignite = new { type = "string", description = "stash|memo: AutoIgnition note" },
            plan = new { type = "string", description = "stash|memo: Task Manager focus note" },
            note = new { type = "string", description = "reconcile|align|ready|gate|steer: optional decision note" },
            to = new { type = "string", description = "gate: pull|reconcile|align|ready" },
            strict = new { type = "boolean", description = "recall: true = force pull even when SSOT sufficient" },
            limit = new { type = "integer", description = "line: last N memos (default 5, max 50)" }
        }
    }),
    Meta("cdp_domain", "Domain ownership soft organ — reconstruction chains [A] from .cdp/domain/*.md (name→edges→entry→≠). Dig-before-ask surface. op=scene|pulse|list|card. Alias go=domain|domain_desk. Not W-essay; op=card id= for one-card [C].",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|pulse|list|card" },
                id = new { type = "string", description = "card: domain card id (tm|ignite|cockpit|pressure)" },
                focus = new { type = "string", description = "pulse/scene: focus hint for card scoring" },
                hint = new { type = "string", description = "alias of focus=" },
                card = new { type = "string", description = "alias of id=" }
            }
        }),
    Meta("cdp_icm", "ICM discovery for on-demand GUI CDP client (ADR-0019). op=scene|aliases|resolve|invoke. Melody command_id → CDP tool via IdeCommandAliasMap; invoke uses ExecuteAliasedAsync. Alias go=icm|icm_desk. Does not mutate IntentMelody.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|aliases|resolve|invoke" },
            command_id = new { type = "string", description = "resolve|invoke: Melody or CDP command_id" },
            id = new { type = "string", description = "alias of command_id" },
            command = new { type = "string", description = "alias of command_id" }
        }
    }),
    Meta("cdp_cockpit_host", "Anchor Start/Stop — operator GUI cockpit host. op=scene|start|stop. Config: [cockpit_host] exe in cdp-mcp.toml; path= overrides once; CDP_COCKPIT_HOST_EXE env is escape only. Stop kills host pid only (MCP stays). Alias go=cockpit_start|cockpit_stop|cockpit_host. Does not mutate Melody/settings.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|start|stop" },
            path = new { type = "string", description = "start: exe path override" },
            exe = new { type = "string", description = "alias of path" },
            args = new { type = "string", description = "start: process arguments" }
        }
    }),
    ];
}
