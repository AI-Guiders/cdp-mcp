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
    Meta("cdp_glass", "Agent surface parity desk — Glass WPF co-presence RPC (cmd/reply latches). Full debt: layout|appearance|colors|highlight|focus|click|set_text|send_keys|set_control_layout|set_panel_size. v0: scene|layout. Alias go=surface_desk. Not webcam/PrintWindow.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|layout|appearance|colors|highlight|focus|click|set_text|send_keys|set_control_layout|set_panel_size" },
            name = new { type = "string", description = "control name from layout (drive/aim ops)" },
            text = new { type = "string", description = "set_text" },
            keys = new { type = "string", description = "send_keys e.g. Ctrl+Enter" },
            layout = new { type = "string", description = "set_control_layout JSON" },
            panel = new { type = "string", description = "set_panel_size panel id" },
            width = new { type = "integer" },
            height = new { type = "integer" },
            timeout_ms = new { type = "integer", description = "layout RPC wait (default 8000)" }
        }
    }),
    Meta("cdp_webcam", "Sense desk — in-proc Shared+OpenCv+NAudio+Whisper. op=scene|frame|burst|av|screen|window|window_list|audio|transcribe|ocr|analyze. window: HWND PNG via PrintWindow (process=|title=|hwnd=). Alias go=webcam_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|frame|burst|av|screen|window|window_list|audio|transcribe|ocr|analyze" },
            hwnd = new { type = "string", description = "window: HWND decimal or 0xhex" },
            process = new { type = "string", description = "window/window_list: process name filter (e.g. CDP.GlassCockpit.Windows)" },
            title = new { type = "string", description = "window/window_list: title substring" },
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
