using System.Text.Json;
using Cdp.Core;
using Cdp.Lsp;
using Cdp.ScriptableIde;
using Tomlyn;

namespace CdpMcp;
internal sealed partial class CdpSettings
{
    private static IReadOnlyList<LspLaunchPreset> BuildLspPresets(CdpTomlLspPreset[]? src)
    {
        if (src is not { Length: > 0 })
            return LspLaunchPreset.BuiltInDefaults;
        var list = new List<LspLaunchPreset>();
        foreach (var row in src)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Command))
                continue;
            list.Add(new LspLaunchPreset { Id = row.Id.Trim().ToLowerInvariant(), Command = row.Command.Trim(), Args = row.Args ?? [], LanguageIds = row.LanguageIds is { Length: > 0 } lids ? lids.Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).ToArray() : [row.Id.Trim().ToLowerInvariant()], RootMarkers = row.RootMarkers ?? [] });
        }

        return list.Count > 0 ? list : LspLaunchPreset.BuiltInDefaults;
    }

    private static LanguageRegistry BuildLanguageRegistry(CdpTomlLanguages? src)
    {
        if (src is null)
            return LanguageRegistry.Default;
        var defaults = LanguageRegistry.Default;
        var ids = src.Ids is { Length: > 0 } listed ? listed.Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).ToArray() : defaults.Ids.ToArray();
        var aliases = new List<KeyValuePair<string, string>>();
        if (src.Aliases is { Count: > 0 })
        {
            foreach (var(alias, id)in src.Aliases)
                aliases.Add(new(alias, id));
        }
        else
        {
            aliases.AddRange([new("cs", CdpLanguages.Csharp), new("c#", CdpLanguages.Csharp), new("ts", CdpLanguages.Typescript), new("tsx", CdpLanguages.Typescript), new("py", CdpLanguages.Python), new("pas", CdpLanguages.Delphi), new("objectpascal", CdpLanguages.Delphi), ]);
        }

        IEnumerable<LanguageDetectRule> rules;
        if (src.Detect is { Length: > 0 })
        {
            rules = src.Detect.Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Kind)).Select(d => new LanguageDetectRule(d.Id!.Trim().ToLowerInvariant(), d.Kind!.Trim().ToLowerInvariant(), d.Priority ?? 100, string.IsNullOrWhiteSpace(d.Extension) ? null : d.Extension.Trim(), string.IsNullOrWhiteSpace(d.FileName) ? null : d.FileName.Trim()));
        }
        else
        {
            rules = defaults.DetectRules;
        }

        return new LanguageRegistry(ids, aliases, rules);
    }

    private static MemoryFacetSettings Facet(CdpTomlFacet? src, string[] defaults) => new()
    {
        Enabled = src?.Enabled ?? true,
        Roots = src?.Roots is { Length: > 0 } roots ? roots : defaults
    };
    private static MemoryToggleSettings Enabled(CdpTomlToggle? src) => new()
    {
        Enabled = src?.Enabled ?? true
    };
}