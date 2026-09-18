#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>Meta <c>cdp_suggest</c> — ADR-0229 stateless attach step menu (ship-61b).</summary>
internal static partial class MetaToolCatalog
{
    static IEnumerable<Tool> CoreSuggest() =>
    [
    Meta("cdp_suggest", "[A] Agent UX — stateless attach step menu (CDP-ADR-0229). Generation vs choice: federation brokers return numbered picks; model selects n/value, not bracket DSL. op=scene (AttachSchema catalog) | choices verb=|step=|suggestion_id= partial= path= workspace=. Chain peek lines[].anchor first. Alias go=suggest.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|choices (default scene)" },
            verb = new { type = "string", description = "choices cold-start: error|issue|document|code|nav|manual" },
            step = new { type = "string", description = "choices: pick_file|pick_member|pick_diagnostic|pick_kind|…" },
            step_id = new { type = "string", description = "Alias of step=" },
            suggestion_id = new { type = "string", description = "choices: federation.attach.* broker id (alias suggestion=)" },
            suggestion = new { type = "string", description = "Alias of suggestion_id=" },
            partial = new { type = "string", description = "choices filter (aliases q=, query=)" },
            q = new { type = "string", description = "Alias of partial=" },
            query = new { type = "string", description = "Alias of partial=" },
            path = new { type = "string", description = "choices context for member/file steps (alias file=)" },
            file = new { type = "string", description = "Alias of path=" },
            workspace = new { type = "string", description = "Session anchor override (default SolutionOrProjectPath|ProjectRoot after cdp_open)" },
            workspace_anchor = new { type = "string", description = "Alias of workspace=" }
        }
    })
    ];
}
