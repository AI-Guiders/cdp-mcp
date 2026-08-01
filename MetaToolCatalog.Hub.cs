#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog peeled from Program top-level (soft-warn).</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> HubShell() =>
    [
    Meta("cdp_tools", "[A] Shortlist catalog=f(phase,object[,intent][,language]) — agent command palette preview.", new
    {
        type = "object",
        properties = new
        {
            phase = new { type = "string" },
            @object = new { type = "string" },
            intent = new { type = "string" },
            language = new { type = "string" },
            limit = new { type = "integer" }
        }
    }),
    Meta("cdp_cockpit", "[A] Agent IDE desk — Scan Pattern seats + view once (ADR 0191/0193). Slim alert=sa pulse (sit/locus/layout). [C] go_detail=full|pane_full= (one seat on pulse). seats_detail=full alone stays pulse (refused). World channel replaces on M. Cold auto-restore.", new
    {
        type = "object",
        properties = new
        {
            mfd = new { type = "string", description = "Legacy alias: nav→desk_detail=nav; sys|chk|gates→soft organs (same as go=). Prefer go=sys|chk|gates. Alias: page=." },
            page = new { type = "string", description = "Alias of mfd." },
            locus = new { type = "string", description = "Focus locus id from loci[] (e.g. git:scm, shell:main, buffer:doc-1, browser:net)." },
            focus = new { type = "string", description = "Alias of locus." },
            go = new { type = "string", description = "Desk verb → organ; in seats mode places into P|F|M by policy. Alias: do=." },
            @do = new { type = "string", description = "Alias of go." },
            cmd = new { type = "string", description = "REPL line: \"go browser\" | \"layout cockpit\" | \"seat m git\" | \"clear\". Alias: line=|repl=." },
            line = new { type = "string", description = "Alias of cmd." },
            repl = new { type = "string", description = "Alias of cmd." },
            go_args = new { type = "object", description = "Optional args merged into the target organ tool." },
            go_detail = new { type = "string", description = "[A] pulse (default) | [C] full = organ dump in go.result only (desk stays fast-path)." },
            layout = new { type = "string", description = "Seat preset: cockpit | code+net | code+shell | code+git | desk. Sticky replace-in-seat." },
            seat = new { type = "string", description = "Explicit seat: p|forward|m (with organ=)." },
            organ = new { type = "string", description = "Organ pin for seat= (or pin=)." },
            pins = new { description = "Seats mode: scan-order fill P,F,M. Tiles mode: sticky pin list." },
            tiles = new { description = "Alias of pins." },
            pin = new { description = "Tiles mode: add pin(s). Seats: prefer seat=+organ=." },
            pin_clear = new { type = "boolean", description = "Clear seats/pins." },
            clear_pins = new { type = "boolean", description = "Alias of pin_clear." },
            seat_clear = new { type = "boolean", description = "Alias of pin_clear (seats)." },
            pane_full = new { type = "string", description = "[C] Which seat/pin gets one full dump on pulse (no full BuildAsync spray)." },
            full_pane = new { type = "string", description = "Alias of pane_full." },
            seats_detail = new { type = "string", description = "[A] compact (default: view+slots). full alone refused early (pulse path + thrash); use pane_full=<seat|organ> for one dump on pulse." },
            view_detail = new { type = "string", description = "Alias of seats_detail." },
            desk_detail = new { type = "string", description = "slim (default: omit loci[]/go_verbs[]) | nav | full. Alias: nav_detail=." },
            nav_detail = new { type = "string", description = "Alias of desk_detail." },
            include_submodules = new { type = "boolean", description = "Pass through to git_scene (default false)." },
            no_restore = new { type = "boolean", description = "Skip once-per-process cold auto desk bookmark restore (default false)." }
        }
    }),
    Meta("cdp_session", "[A] Session plane: context + shortlist + health + continuity (pack omitted by default). [C/W] include_pack=true embeds definitions/process/procedure dogfood.", new
    {
        type = "object",
        properties = new
        {
            explain_tool = new { type = "string", description = "Optional: why this tool is hidden/visible." },
            include_debug = new { type = "boolean", description = "[C] Include debug_stop_context when debug mounted (default true)." },
            include_pack = new { type = "boolean", description = "[C/W] Embed LLM-native pack process+procedure+debug-radius (default false = A)." },
            pack_id = new { type = "string", description = "Pack id when include_pack=true (default epistemic-scene)." },
            process_id = new { type = "string", description = "Process id when include_pack=true (default bug-radius-shrink)." },
            procedure_id = new { type = "string", description = "Optional when-card id when include_pack=true." },
            shortlist_limit = new { type = "integer", description = "Shortlist size in snapshot (default 12)." }
        }
    }),
    Meta("cdp_work", "Intent workspace + buffer + debug escape. op=intent_*|stage_*|scene_*|status OR buffer_* OR debug_scene|debug_bp_add|debug_bp_list|debug_launch|… (when host omits cdp_buffer/cdp_debug).", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "intent_*|stage_*|scene_*|status|buffer_*|debug_scene|debug_bp_add|debug_bp_remove|debug_bp_set|debug_bp_list|debug_bp_clear|debug_launch|debug_attach|debug_continue|debug_stop|debug_stop_context|…" },
            title = new { type = "string" },
            intent_id = new { type = "string" },
            stage_id = new { type = "string" },
            parent_id = new { type = "string" },
            scene_name = new { type = "string", description = "For stage_upsert bind; also alias of name for scene ops." },
            name = new { type = "string", description = "Scene name for park/switch." },
            status = new { type = "string", description = "pending|active|done|parked|deferred" },
            loot = new { type = "string" },
            focus_path = new { type = "string" },
            focus_line = new { type = "integer" },
            bind_stage_id = new { type = "string" },
            job_json = new { type = "string", description = "For stage_enqueue: {kind,file_path,solution_or_project_path,...}" },
            start_job = new { type = "boolean", description = "For stage_enqueue: start background IdeReport job (default true)." },
            path = new { type = "string", description = "buffer_* file path; debug_bp_add/remove source path" },
            file_path = new { type = "string", description = "Alias of path for debug bp_*" },
            line = new { type = "integer", description = "debug_bp_add/remove 1-based line" },
            condition = new { type = "string", description = "optional breakpoint condition" },
            workspace_path = new { type = "string", description = "debug_*: optional; session default after cdp_open" },
            target_path = new { type = "string", description = "debug_*: optional; session .csproj/.sln" },
            breakpoints = new { type = "array", description = "debug_bp_set only", items = new { type = "object" } },
            doc_id = new { type = "string", description = "buffer_*: open buffer id" },
            diagnose = new { type = "boolean" },
            flush = new { type = "boolean" },
            refresh = new { type = "boolean" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" },
            start_column = new { type = "integer" },
            end_column = new { type = "integer" },
            edit_op = new { type = "string", description = "buffer_edit: set_text|replace|replace_range" },
            text = new { type = "string" },
            old_string = new { type = "string" },
            new_string = new { type = "string" }
        },
        required = new[] { "op" }
    }),
    Meta("cdp_csx_check", "Compile CSX against allowlisted ScriptGlobals (Debug/Roslyn/Git/Verify/Mutate/Anui/Execution/Help). No tool dispatch. Returns DiagnosticItems with anchors.", new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "CSX source (preferred)." },
            path = new { type = "string", description = "Optional path to .csx file if code omitted." }
        }
    }),
    Meta("cdp_csx_help", "Live CSX API help from XML docs (not a static man). op=toc|of. Prefer before inventing Symbol/SemanticMap APIs.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "toc (default) | of" },
            path = new { type = "string", description = "For op=of: Symbol, SemanticMap, Symbol.Named, Help, …" },
            max = new { type = "integer", description = "Cap facade/member rows (default 48 toc / 40 of)." }
        }
    }),
    Meta("cdp_evidence", "Project any pipe (build/test/publish/shell/auto) to evidence/v0 with Anchor wires — click locus, no line guessing.", new
    {
        type = "object",
        properties = new
        {
            kind = new { type = "string", description = "auto|build|test|publish|shell|csx|generic (default auto)" },
            text = new { type = "string", description = "Raw stdout/stderr/log body to project." },
            path = new { type = "string", description = "Optional file path if text omitted." }
        }
    }),
    Meta("cdp_csx_run", "Run CSX via ScriptHost. mode=run|dry_run. Dispatches to mounted domains (roslyn/git/debug/…).", new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "CSX source (preferred)." },
            path = new { type = "string", description = "Optional path to .csx file if code omitted." },
            mode = new { type = "string", description = "run (default) | dry_run" },
            workspace_path = new { type = "string", description = "Plan.PrimaryRoot / WorkRoot for Fs + path remap (default cwd)." }
        }
    }),
    Meta("cdp_csx_run_plan", "Sandbox scoped to open project (GitRoot+PlanScope); overlay primary WIP under scope; promote merges plan delta (dirty elsewhere OK). Then cdp_csx_promote|discard.", new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string" },
            path = new { type = "string" },
            workspace_path = new { type = "string", description = "Entry path (optional if cdp_open session). Resolved via git rev-parse --show-toplevel." },
            scope = new { type = "string", description = "Optional focus dir/file for PlanScope (default: session project root)." },
            promote_policy = new { type = "string", description = "overlap_safe (default) | strict_clean" }
        }
    }),
    Meta("cdp_csx_discard", "Remove worktree for plan_id; primary unchanged.", new
    {
        type = "object",
        properties = new { plan_id = new { type = "string" } },
        required = new[] { "plan_id" }
    }),
    Meta("cdp_csx_promote", "Apply plan delta to primary (default overlap_safe: dirty elsewhere OK; strict_clean = refuse any dirty). File sync of plan paths; conflict check first.", new
    {
        type = "object",
        properties = new
        {
            plan_id = new { type = "string" },
            promote_policy = new { type = "string", description = "overlap_safe | strict_clean (optional override)" }
        },
        required = new[] { "plan_id" }
    }),
    Meta("cdp_shell_scene", "[A] Agent terminal habitat map: all tabs (id, shell, cwd, state, last cmd/exit, preview). Prefer over switch→watch→switch.", new
    {
        type = "object",
        properties = new { }
    }),
    Meta("cdp_shell_run", "Run in named tab. Prefer argv[] (harness quotes). Or command string for pipes/| . Session cwd.", new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "Raw shell line (pipes ok). Ignored if argv is non-empty." },
            argv = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Structured args: [program, arg1, …]. Harness joins with shell-safe quoting."
            },
            tab = new { type = "string", description = "Tab id (default main). letters/digits/_- max 32." },
            cwd = new { type = "string", description = "Working directory (persists on tab). Alias: working_directory." },
            working_directory = new { type = "string", description = "Alias of cwd (Cursor Shell habit)." },
            shell = new { type = "string", description = "Prefer: pwsh | cmd | or unix shell path." },
            codepage = new { type = "integer", description = "Console/pipe code page; sticky on tab. Default 65001 (UTF-8)." },
            timeout_seconds = new { type = "integer", description = "1..600 (default 60). Ignored when background=true." },
            background = new { type = "boolean", description = "true = long-run in CDP process; poll scene/last; kill to stop." }
        }
    }),
    Meta("cdp_shell_history", "[A] Last N commands for a tab (cmd/cwd/exit/preview; no full stdout dump).", new
    {
        type = "object",
        properties = new
        {
            tab = new { type = "string" },
            n = new { type = "integer", description = "1..50 (default 20)." }
        }
    }),
    Meta("cdp_shell_rerun", "Re-run history entry (default last) on a tab — ↑ analogue.", new
    {
        type = "object",
        properties = new
        {
            tab = new { type = "string" },
            index = new { type = "integer", description = "History index; omit = last." },
            timeout_seconds = new { type = "integer" },
            background = new { type = "boolean" }
        }
    }),
    Meta("cdp_shell_last", "[C] Last result body for a tab (capped stdout/stderr). While running: live buffers.", new
    {
        type = "object",
        properties = new
        {
            tab = new { type = "string" },
            max_chars = new { type = "integer", description = "Cap per stream (default 12000)." }
        }
    }),
    Meta("cdp_shell_which", "[A] Active shell kind + exe + cwd (+ pid/state) for a tab.", new
    {
        type = "object",
        properties = new { tab = new { type = "string" } }
    }),
    Meta("cdp_shell_kill", "Kill running process on a tab (process tree).", new
    {
        type = "object",
        properties = new { tab = new { type = "string" } }
    }),
    Meta("cdp_shell_close", "Close tab (kills if running); removes it from the habitat scene.", new
    {
        type = "object",
        properties = new { tab = new { type = "string" } }
    })
    ];
}
