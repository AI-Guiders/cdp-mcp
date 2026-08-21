#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog — Soft ops peel (soft-warn): files→cockpit_host.</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> SoftOps() =>
    [
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
    Meta("cdp_ignite", "AutoIgnition continuity — Guest Autoi still CDT→Cursor Composer (requires --remote-debugging-port=9222); wake latch SSOT ignite-wake-LATEST.json (habitat|composer). Plain timer: PF duplex busy|composing → prefer habitat skip CDT; autonomous+idle PF + citizen invite ready → citizen Turn consume skip CDT (prefer_citizen 0.5.551); else stamp habitat SSOT then Guest CDT fallthrough (0.5.532). System wakes (remount/HILD/OOM/tool) stay Composer adapter + Intercom mirror. Not Cognitive CDP. op=scene|probe|chats|send|arm|disarm|list|hygiene|plateau|continuity|resume|autonomous|hild|halt|await_partner. ARM: when=build_finished|test_finished|shell_finished|peer_ship|human_away|timer task= (TM label only). HILD: Composer text idle 30s on Voice → human_away once → AutoI wake (default ARMED; op=hild_off). Default charge=minimal: canonical wake text + amnesia/compaction postfix at fire (no TM body in composer). charge=custom only for legacy templates. last_once=/await_partner: fire once → awaiting latch when autonomous off; under autonomous last_once does not invent-ban — arm tips: insurance ≠ park while TM leaf started (0.5.537); continuity scene + canonical charge same (0.5.538); autonomous last_once timer clamped ≤3m; ≤3s when ContinuityFlight.Fly or HILD away_latched (0.5.539–0.5.543); TimerLoop leaf Fly pull-forward already-armed (0.5.545). halt=stop-world (autonomous+HILD off, clear arms, await partner). autonomous default ARMED: auto LeafPlateau does not await_partner — seed-wake instead. Alias go=ignite_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|probe|chats|send|arm|disarm|list|autonomous|autonomous_on|autonomous_off|hild|hild_on|hild_off|resume|continuity|halt|await_partner|await_operator" },
            message = new { type = "string", description = "send: optional override; arm: ignored unless charge=custom" },
            task = new { type = "string", description = "arm: Task Manager label (SSOT); not injected into composer" },
            charge = new { type = "string", description = "arm: minimal (default)|custom|legacy — minimal fires canonical+amnesia postfix" },
            when = new { type = "string", description = "arm: build_finished|test_finished|shell_finished|peer_ship|human_away|timer" },
            @event = new { type = "string", description = "alias of when=" },
            @in = new { type = "string", description = "arm timer: 30s|5m|2h" },
            chat = new { type = "string", description = "optional chat title substring" },
            id = new { type = "string", description = "disarm id= / arm custom id" },
            all = new { type = "boolean", description = "disarm all=true (under autonomous: except autonomy means unless force)" },
            force = new { type = "boolean", description = "disarm: clear autonomy means too; arm: override epic-closed / last_once gates" },
            last_once = new { type = "boolean", description = "arm: fire once → awaiting latch when autonomous off; under autonomous no invent-ban" },
            armed = new { type = "boolean", description = "autonomous|hild: true|false latch (default ARMED)" },
            ok_only = new { type = "boolean", description = "arm: fire only on green build/test (default true)" },
            settle_seconds = new { type = "integer", description = "arm: delay before CDT send after event (default 8)" },
            port = new { type = "integer", description = "CDT port (default 9222)" },
            wait_seconds = new { type = "integer", description = "max wait for idle (not Stop/Queue), default 90" }
        }
    }),
    Meta("cdp_glass", "Glass cabin SA + surface parity. op=scene (default) → cabin_sa/v0 pulse (why/next/course/seats/mfd/land/shared/ignite/alert/file_situ) without PNG. RPC: layout|appearance|colors|highlight|focus|click|set_text|send_keys|palette|run|…. Alias go=glass_scene|surface_desk|cabin_sa. Not webcam/PrintWindow.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|layout|appearance|colors|highlight|focus|click|set_text|send_keys|palette|run|action|set_control_layout|set_panel_size|request_confirmation" },
            name = new { type = "string", description = "control name from layout (drive/aim ops)" },
            text = new { type = "string", description = "set_text; run: slash line e.g. /status" },
            keys = new { type = "string", description = "send_keys e.g. Ctrl+Enter" },
            layout = new { type = "string", description = "set_control_layout JSON" },
            panel = new { type = "string", description = "set_panel_size: pfd_region|mfd_region|intercom" },
            width = new { type = "integer" },
            height = new { type = "integer" },
            message = new { type = "string", description = "request_confirmation prompt" },
            query = new { type = "string", description = "palette: filter text; run: alias of text" },
            execute = new { type = "boolean", description = "palette: execute top hit" },
            action = new { type = "string", description = "run: Glass action id e.g. mfd_git|slash_status" },
            command_id = new { type = "string", description = "run: CIDE melody command_id → allowlisted Glass peel" },
            start = new { type = "integer", description = "run select: start line" },
            end = new { type = "integer", description = "run select: end line" },
            timeout_ms = new { type = "integer", description = "RPC wait (default 8000; confirm 120000)" }
        }
    }),
    Meta("cdp_see", "Agent vision — attach image as MCP ImageContent (ToolMediaOutbox). path=|file= local PNG/JPEG/WebP; url= http(s) download (+ optional .cdp/evidence/see-cache). op=scene|see. Alias go=see|see_desk. World dig: paper figures / UI refs / Glass evidence — not Lynx, not host-only Read.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|see (default see when path/url set)" },
            path = new { type = "string", description = "local image path (absolute or under ProjectRoot)" },
            file = new { type = "string", description = "alias of path=" },
            file_path = new { type = "string", description = "alias of path=" },
            image = new { type = "string", description = "alias of path=" },
            url = new { type = "string", description = "http(s) or file:// image URL" },
            href = new { type = "string", description = "alias of url=" },
            src = new { type = "string", description = "alias of url=" }
        }
    }),
    Meta("cdp_webcam", "Sense desk — in-proc Shared+OpenCv+NAudio+Whisper. op=scene|frame|burst|av|screen|window|window_list|audio|transcribe|ocr|analyze. window: HWND PNG via PrintWindow (process=|title=|hwnd=); maximize=true = max→shot→restore peel. Alias go=webcam_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|frame|burst|av|screen|window|window_list|audio|transcribe|ocr|analyze" },
            hwnd = new { type = "string", description = "window: HWND decimal or 0xhex" },
            process = new { type = "string", description = "window/window_list: process name filter (e.g. CDP.GlassCockpit.Windows)" },
            title = new { type = "string", description = "window/window_list: title substring" },
            maximize = new { type = "boolean", description = "window: ShowWindow maximize → PrintWindow → restore placement (dogfood peel; alias enlarge)" },
            enlarge = new { type = "boolean", description = "alias of maximize=" },
            max = new { type = "integer", description = "window_list: cap (default 40)" },
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
    Meta("cdp_pressure", "L1 pre-compact prep desk. On pressure notify (~2–3 turns before host summarization): op=arm → checklist → op=stash body= (also appends memo line; optional wave= JSON array or ## wave in body). Anti-compaction: op=memo body= / op=line. Recall gate (ADR-0024): op=recall → ready when SSOT (body+plan/ignite) else pull → op=reconcile → op=align → op=ready; strict=true forces pull; op=steer|ssot|fast shortcuts. Must axes: AutoIgnition re-ARM, Task Manager, CDP habitat, Domain (.cdp/domain). Alias go=pressure_desk|pressure. Does not offer export ritual to operator.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|arm|stash|memo|line|clear|disarm|recall|reconcile|steer|ssot|fast|align|ready|gate" },
            body = new { type = "string", description = "stash|memo: markdown/text — goal, decisions, open, next, ignite, plan; optional ## wave section" },
            text = new { type = "string", description = "alias of body=" },
            wave = new { type = "string", description = "stash: JSON array string of wave labels, e.g. [\"a\",\"b\"] — also parsed from ## wave in body" },
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
    Meta("cdp_calendar", "Machine-local calendar/clock soft organ. op=scene|pulse|month — daypart + TZ + month grid + epic deadlines. Alias go=calendar|clock. Cockpit slim always exposes clock= pulse.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|pulse|month" }
            }
        }),
    Meta("cdp_freshness", "KB freshness MLP soft desk — harness walks watchlist URLs; returns Digest/Atom-shaped entries (not raw HTML). Digest ≠ Проверено stamp. op=scene|pulse|watchlist|scan|digest|explain|aliases|clear|nrt|schedule|arm|disarm|tick. alias=/urls=/domain=. Alias go=freshness|freshness_desk.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|pulse|watchlist|scan|digest|explain|aliases" },
                alias = new { type = "string", description = "built-in alias id (baseline2026|php|laravel|avalonia|node) or CSV" },
                aliases = new { type = "string", description = "alias of alias=" },
                urls = new { type = "string", description = "CSV of https URLs" },
                url = new { type = "string", description = "single URL (explain|scan)" },
                domain = new { type = "string", description = "worlds/<domain> slug — extract https from md" },
                world = new { type = "string", description = "alias of domain=" },
                path = new { type = "string", description = "md/file path — extract https" },
                file = new { type = "string", description = "alias of path=" },
                take = new { type = "integer", description = "scan: max URLs (default 12, max 40)" },
                persist = new { type = "boolean", description = "scan: write cache (default true)" }
            }
        }),
    Meta("cdp_env_readiness", "Environment Readiness soft desk — installed/running/PATH/agent-notes/CDP backends. CIDE quarry rows + CDP habitat (Roslyn, seat, freshness). op=scene|pulse|rows|scan. Alias go=env|environment|environment_readiness.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|pulse|rows|scan" },
            }
        }),
    Meta("cdp_ide_health", "IDE Health soft desk — build/tests/debug/git strip fold (platform CCU quarry). op=scene|pulse|segments. Alias go=ide_health|workspace_health|wh.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|pulse|segments|strip" },
            }
        }),
    Meta("cdp_rules", "Healthy-agent standing rules [A] from .cdp/rules/*.md (ε body, dig/parallel, not biped). op=scene|pulse|list|card. Alias go=rules|standing. Remount Autoi appends Standing appendix. Not eQRH; not Cursor alwaysApply dump.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|pulse|list|card" },
                id = new { type = "string", description = "card: rule id (healthy-agent)" },
                focus = new { type = "string", description = "pulse/scene: focus hint" },
                hint = new { type = "string", description = "alias of focus=" },
                card = new { type = "string", description = "alias of id=" }
            }
        }),
    Meta("cdp_inventory", "Throughput inventory [A]: dense gap list + active wave + batch-size recommend (~8–15). Soft FileLines CLOSED. op=scene|pulse. Alias go=inventory|gaps. Not W-spray; list→batch→ship.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|pulse" }
            }
        }),
    Meta("cdp_verify_wave", "Wave ship checklist [A]: tests, dual hard recipe, domain stamp, git, ignite re-ARM. Does NOT KillRunning deploy from in-proc. op=scene|pulse. Alias go=verify_wave.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|pulse" }
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
