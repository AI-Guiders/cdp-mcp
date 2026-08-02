#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>cdp_capabilities payload for MetaDispatch.Core (method_lines peel).</summary>
internal static partial class MetaDispatch
{
    static string CapabilitiesJson(MetaDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        _ = callArgs;
        var byDomain = d.ByDomain;
        var allAffordances = d.AllAffordances;
        var settings = d.Settings;
        var SoftOrganMetaNames = d.SoftOrganMetaNames;
        var Pretty = d.Pretty;
        var BuildVisibleTools = d.BuildVisibleTools;
        var BuildMetaTools = d.BuildMetaTools;
        return JsonSerializer.Serialize(new
        {
            catalog = "f(phase,object[,language]); intent ranks",
            phases = Enum.GetNames<CdpPhase>().Select(x => x.ToLowerInvariant()),
            objects = Enum.GetNames<CdpObjectKind>().Select(x => x.ToLowerInvariant()),
            intents = Enum.GetNames<CdpIntent>().Select(x => x.ToLowerInvariant()),
            languages = settings.Languages.Ids,
            affordances = allAffordances.Length,
            domains = byDomain.Keys.OrderBy(x => x).ToArray(),
            list_tools_count = BuildVisibleTools().Count,
            meta_tool_names = BuildMetaTools()
                .Where(t => !SoftOrganMetaNames.Contains(t.Name))
                .Select(t => t.Name)
                .ToArray(),
            soft_organ_meta_hidden = SoftOrganMetaNames.OrderBy(x => x).ToArray(),
            buffer_tool = BuildMetaTools()
                .Where(t => t.Name == "cdp_buffer")
                .Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    input_schema = t.InputSchema
                })
                .FirstOrDefault(),
            debug_tool = BuildMetaTools()
                .Where(t => t.Name == "cdp_debug")
                .Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    input_schema = t.InputSchema
                })
                .FirstOrDefault(),
            layers = new
            {
                memory = new
                {
                    world = FacetCap(settings.Memory.World),
                    project = FacetCap(settings.Memory.Project),
                    task = ToggleCap(settings.Memory.Task),
                    session = ToggleCap(settings.Memory.Session),
                    skill = FacetCap(settings.Memory.Skill),
                    self = new
                    {
                        finding = ToggleCap(settings.Memory.Self.Finding),
                        failure = ToggleCap(settings.Memory.Self.Failure)
                    }
                },
                dev = new
                {
                    debug = ToggleCap(settings.Dev.Debug),
                    build = ToggleCap(settings.Dev.Build),
                    roslyn = ToggleCap(settings.Dev.Roslyn),
                    git = ToggleCap(settings.Dev.Git),
                    codebase_index = ToggleCap(settings.Dev.CodebaseIndex),
                    anui = ToggleCap(settings.Dev.Anui)
                }
            }
        }, Pretty);
    }
}
