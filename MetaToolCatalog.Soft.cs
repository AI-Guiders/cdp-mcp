#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog — Soft organs (refactor→scope); ops peel in Soft.Ops.</summary>
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
    ];
}
