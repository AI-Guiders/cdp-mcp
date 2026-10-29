#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>Meta <c>cdp_peek</c> — ADR-0201 read-only eyes (Core peel).</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> CorePeek() =>
    [
    Meta("cdp_peek", "[A] Read-only file eyes — fast disk ingress (ADR-0201). Prefer over host Read in CDP habitat. No buffer open/diagnostics/corr gate. path= or paths[] (batch ≤8); offset|start_line + limit|lines (default 120, max 500; negative offset from EOF); anchor=/at= land ±pad; query=+glob= rg→peek windows. Returns numbered text + lines[].anchor for sniper chain. bind=true (default) lazy-detects project on first peek.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "File path (rel → ProjectRoot). Alias file=" },
            paths = new { type = "array", items = new { type = "string" }, description = "Batch peek ≤8 files; char budget shared" },
            offset = new { type = "integer", description = "1-based start line; negative = from EOF (Read semantics)" },
            start_line = new { type = "integer", description = "Alias of offset" },
            limit = new { type = "integer", description = "Lines to return (default 120, max 500)" },
            lines = new { type = "integer", description = "Alias of limit" },
            anchor = new { type = "string", description = "Land window around anchor line ±pad (BracketLocate wire)" },
            at = new { type = "string", description = "Alias of anchor" },
            pad = new { type = "integer", description = "Context lines around anchor= (default 20) or find query hits (default 3)" },
            query = new { type = "string", description = "Find+peek: rg needle (alias pattern=, q=)" },
            glob = new { type = "string", description = "Find+peek: rg --glob (e.g. *.cs)" },
            scope = new { type = "string", description = "Path resolve: project (default) | external (absolute outside session)" },
            regex = new { type = "boolean", description = "Find+peek: regex query" },
            ignore_case = new { type = "boolean" },
            max = new { type = "integer", description = "Find+peek: max hit windows (default 5, max 20)" },
            bind = new { type = "boolean", description = "Lazy session project detect from path (default true)" },
            include_anchors = new { type = "boolean", description = "Emit lines[].anchor (default true)" },
            text_only = new { type = "boolean", description = "Omit lines[] array" },
            structured_only = new { type = "boolean", description = "Omit numbered text block" },
            mode = new { type = "string", description = "outline: structural section map for md/json/yaml/toml (cheap markers); else normal file read" }
        }
    }, new
    {
        type = "object",
        properties = new
        {
            schema = new { type = "string", description = "Envelope version." },
            ok = new { type = "boolean" },
            tool = new { type = "string", description = "cdp_peek." },
            mode = new { type = "string", @enum = new[] { "file", "batch", "image", "find", "outline" }, description = "file (single read) | batch (paths[]) | image | find (query=) | outline (structural map)." },
            path = new { type = "string", description = "Absolute path (file/image/outline/error branches)." },
            rel = new { type = "string", description = "Project-relative path." },
            total_lines = new { type = "integer", description = "file/outline: whole-file line count." },
            offset = new { type = "integer", description = "file: 1-based first returned line." },
            limit = new { type = "integer", description = "file: window size." },
            returned = new { type = "integer", description = "file: lines actually returned." },
            has_more = new { type = "boolean", description = "file: more lines below the window." },
            next_offset = new { type = new[] { "integer", "null" }, description = "file: offset= for the next window (null at EOF)." },
            chars = new { type = "integer", description = "file: approximate payload size." },
            text = new { type = new[] { "string", "null" }, description = "file: numbered text block (omit with structured_only=true)." },
            lines = new
            {
                type = new[] { "array", "null" },
                description = "file: per-line records for the mutate chain (omit with text_only=true).",
                items = ToolSchemas.For<CdpPeekChannel.LineEntry>()
            },
            count = new { type = "integer", description = "batch: files peeked | find: hit windows | outline: entries." },
            truncated = new { type = "boolean", description = "batch: shared char budget exhausted." },
            files = new { type = "array", items = new { type = "object" }, description = "batch: per-file results, same shape as a single peek." },
            bytes = new { type = "integer", description = "image: file size | file_too_large: offending size." },
            extension = new { type = "string", description = "image: e.g. .png (cdp_see for vision)." },
            max_bytes = new { type = "integer", description = "file_too_large: cap." },
            query = new { type = "string", description = "find: the needle." },
            regex = new { type = "boolean", description = "find: regex interpretation applied." },
            glob = new { type = new[] { "string", "null" }, description = "find: normalized rg glob." },
            scope = new { type = new[] { "string", "null" }, description = "find: search scope." },
            windows = new { type = "array", items = ToolSchemas.For<CdpPeekChannel.FindWindow>(), description = "find: { hit{anchor,path,rel,line,preview}, peek } per match — anchor feeds the sniper chain." },
            find = new { type = new[] { "object", "null" }, description = "find: raw find report on failure or zero hits." },
            ext = new { type = new[] { "string", "null" }, description = "outline: file extension." },
            kind = new { type = new[] { "string", "null" }, description = "outline: markdown|json|yaml|toml." },
            supported = new { type = "boolean", description = "outline: false when no cheap markers." },
            entries = new { type = "array", items = ToolSchemas.For<CdpPeekChannel.OutlineEntry>(), description = "outline: structural entries (title/line/level)." },
            error = new { type = "string", description = "not_found | binary | file_too_large | batch_too_large | no_store (ok=false)." },
            detail = new { type = "string", description = "Error detail." },
            bind_note = new { type = new[] { "string", "null" }, description = "Lazy project-bind note on first peek." },
            outline_hint = new { type = new[] { "string", "null" }, description = "file: pointer when mode=outline would be cheaper." },
            hint = new { type = "string", description = "Next-step hint (mutate chain / retry / narrow)." }
        },
        required = new[] { "schema", "ok", "tool" }
    })
    ];
}
