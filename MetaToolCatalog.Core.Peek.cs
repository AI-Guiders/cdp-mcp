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
    })
    ];
}
