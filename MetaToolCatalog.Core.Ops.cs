#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog — Core ops peel (soft-warn): recent→sa.</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> CoreOps() =>
    [
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
            include_raw_output = new { type = "boolean", description = "alias of include_raw" },
            background = new { type = "boolean", description = "true (default) = enqueue deploy job, return immediately, AutoIgnition wake on peer_ship. false or wait=true = block (minutes; MCP may timeout). dry_run always sync." },
            durable = new { type = "boolean", description = "true (default for background deploy) = out-of-process durable queue; survives KillRunning/remount. false = in-process Layer 1 only." },
            ignite_arm = new { type = "boolean", description = "When background=true: auto arm when=peer_ship (default true)." },
            wait = new { type = "boolean", description = "true = foreground sync (background=false)." }
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
    Meta("cdp_land", "Land via Family:navigation Anchor wire (ADR 0186). NOT Deep-Link/URI. Pass anchor=[Family:navigation;Command:open|goto|restore|show|go;…]. Nested Anchor:[…] reuses code/xml resolve. Default open|goto quiet (show_face=false); Command:show invites Human Face. Alias go=land.", new
    {
        type = "object",
        properties = new
        {
            anchor = new { type = "string", description = "[Family:navigation;Command:…;Go:…;Anchor:[…]]" },
            at = new { type = "string", description = "Alias of anchor" },
            wire = new { type = "string", description = "Alias of anchor" },
            show_face = new { type = "boolean", description = "Invite Human Glass Face (AvalonEdit + PreferSurface). Default false on open|goto; Command:show defaults true." }
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
            page = new { type = "string", description = "alias of mfd_page" },
            show_face = new { type = "boolean", description = "set: agent Face invite — SelectMfd when true; quiet agent patches leave Human stick" }
        }
    }),
    Meta("cdp_intercom", "Dual-cockpit Intercom voice @PF/@PM. op=scene|send|ack|history|presence|identity. send to=pm body= [channel=crew|radio|dm] [name=] → latch+journal (Face rail filter); name= claims sticky Who. history limit= [query=|contains=|q=] searches journal body+name. NorthStar: #crew · Radio · DM. Alias go=intercom.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|get|inbox|send|ack|history|presence|identity (default scene)" },
            to = new { type = "string", description = "send: pm|pf or @PM|@PF (default pm)" },
            from = new { type = "string", description = "send: optional seat override (default pf); presence/identity: alias of seat" },
            body = new { type = "string", description = "send: message text" },
            message = new { type = "string", description = "send: alias of body" },
            text = new { type = "string", description = "send: alias of body" },
            channel = new { type = "string", description = "send: crew|radio|dm (aliases #crew|direct|1:1); Face feed filter; omit→radio" },
            feed = new { type = "string", description = "send: alias of channel" },
            name = new { type = "string", description = "send/identity: freeform Who / nick (claims sticky on send)" },
            display_name = new { type = "string", description = "alias of name" },
            nick = new { type = "string", description = "alias of name (also send as=)" },
            kind = new { type = "string", description = "send/identity: guest|citizen|operator" },
            action = new { type = "string", description = "identity: get|set|clear (default get)" },
            id = new { type = "string", description = "ack: optional message id" },
            limit = new { type = "integer", description = "history: last N messages (default 20, max 200); with query= = max hits" },
            query = new { type = "string", description = "history: contains search on body+name (aliases contains=|q=)" },
            contains = new { type = "string", description = "history: alias of query=" },
            q = new { type = "string", description = "history: alias of query=" },
            seat = new { type = "string", description = "presence/identity: pf|pm (default pf)" },
            state = new { type = "string", description = "presence: idle|composing|busy" },
            status = new { type = "string", description = "presence: alias of state" },
            ttl_s = new { type = "integer", description = "presence: optional TTL seconds (composing/busy stale after)" }
        }
    }),
    Meta("cdp_citizen", "Citizen completions host (ADR-0028). op=scene|keys|turn|history|clear|sticky. turn message= [mode=wire|dialog] [image_path=] [history=true] [reset=true] [sticky_key=] [sticky_value=] [board=] [dry_run=true] [model=] — dialog=prose peer + multi-turn memory + sticky pins; wire=hands @intent. Vision: image_path= or prior cdp_see latch → OpenAI-compat image_url (default model Qwen/Qwen3.6-35B-A3B). Alias go=citizen.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|keys|turn|history|clear (default scene)" },
            message = new { type = "string", description = "turn: user text" },
            body = new { type = "string", description = "turn: alias of message" },
            mode = new { type = "string", description = "turn: wire (default, hands @intent) | dialog (prose peer; aliases prose|chat|talk|peer)" },
            history = new { type = "boolean", description = "turn dialog: include prior turns (default true); false = amnesiac turn" },
            reset = new { type = "boolean", description = "turn: clear dialog history before this message" },
            clear_history = new { type = "boolean", description = "turn: alias of reset" },
            board = new { type = "string", description = "turn: optional desk board lines (newline seat rows); omit -> auto-bind live desk seats + TM pulse" },
            sa = new { type = "string", description = "turn: optional sa field" },
            peer = new { type = "string", description = "turn: optional peer field" },
            next = new { type = "string", description = "turn: optional next field" },
            tm = new { type = "string", description = "turn: optional tm field" },
            model = new { type = "string", description = "turn: model id (default: Cloud.ru FM or Anthropic sonnet by provider; vision auto → Qwen3.6)" },
            image_path = new { type = "string", description = "turn: local PNG/JPEG/WebP → multimodal image_url (also aliases image=|see_path=|vision_path=)" },
            image = new { type = "string", description = "turn: alias of image_path=" },
            see_path = new { type = "string", description = "turn: alias of image_path=" },
            vision_path = new { type = "string", description = "turn: alias of image_path=" },
            dry_run = new { type = "boolean", description = "turn: build messages only, no provider call" },
            execute = new { type = "boolean", description = "turn: host-execute @intent routes (default: live=true, dry_run=false); place go/drill + open path" },
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
    Meta("cdp_browser", "Agent internet browser in CDP (ADR 0188) — lynx + Chromium UA spoof. NOT Cursor Browser. op=scene|which|open|search|dump|links|follow|back|forward|close. Default peer dig = lynx only (no Glass Face). Face latch for operator eyes: face=true|show=true|share=true|to=operator (or op=show). Search default=DDG HTML. Alias go=scene_internet_browser.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene (default) | which | open | search | dump | links | follow | back | forward | close | show (open+Face)" },
            url = new { type = "string", description = "open: https://… (http/file ok; bare host → https://)" },
            q = new { type = "string", description = "search: query text" },
            query = new { type = "string", description = "search: alias of q" },
            face = new { type = "string", description = "true|1 — latch Glass WebAiPortal for operator eyes (default peer dig skips Face)" },
            show = new { type = "string", description = "alias of face=true" },
            share = new { type = "string", description = "alias of face=true" },
            to = new { type = "string", description = "operator|human|face — Face latch (same as face=true)" },
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
