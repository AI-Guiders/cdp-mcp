#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog peeled from Program top-level (soft-warn).</summary>
internal static partial class MetaToolCatalog
{
    /// <summary>Immutable until process recycle / deploy — Build() once (L4 amortise).</summary>
    public static IReadOnlyList<Tool> All { get; } = BuildOnce();

    public static List<Tool> Build() => All.ToList();

    static IReadOnlyList<Tool> BuildOnce() =>
        [..Core(), ..CorePeek(), ..CoreSuggest(), ..CoreOps(), ..SoftInstruments(), ..SoftOps(), ..IdeLifecycle(), ..IdePkg(), ..HubShell()];

    static Tool Meta(string name, string desc, object schema) => new()
    {
        Name = name,
        Description = desc,
        InputSchema = JsonSerializer.SerializeToElement(schema)
    };
}
