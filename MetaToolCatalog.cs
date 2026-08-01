#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>ListTools Meta catalog peeled from Program top-level (soft-warn).</summary>
internal static partial class MetaToolCatalog
{
    public static List<Tool> Build() =>
        [..Core(), ..CoreOps(), ..SoftOrgans(), ..SoftOps(), ..IdeLifecycle(), ..HubShell()];

    static Tool Meta(string name, string desc, object schema) => new()
    {
        Name = name,
        Description = desc,
        InputSchema = JsonSerializer.SerializeToElement(schema)
    };
}
