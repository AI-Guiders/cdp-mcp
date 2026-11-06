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
        var SoftInstrumentMetaNames = d.SoftInstrumentMetaNames;
        var Pretty = d.Pretty;
        var BuildVisibleTools = d.BuildVisibleTools;
        var BuildMetaTools = d.BuildMetaTools;

        object? SlimToolRef(string name) =>
            BuildMetaTools().FirstOrDefault(t => t.Name == name) is { } t
                ? new { name = t.Name, schema_hint = "cdp_man tool=" + t.Name }
                : null;

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
                .Where(t => !SoftInstrumentMetaNames.Contains(t.Name))
                .Select(t => t.Name)
                .ToArray(),
            soft_organ_meta_hidden = SoftInstrumentMetaNames.OrderBy(x => x).ToArray(),
            // Slim pointers, not schemas: the full input_schema poem (~40KB with the
            // buffer plane) belongs to cdp_man tool=<name> on demand — capabilities
            // is a catalog, agents pull detail per tool when they need it.
            buffer_tool = SlimToolRef("cdp_buffer"),
            debug_tool = SlimToolRef("cdp_debug"),
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
