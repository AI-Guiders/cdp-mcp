#nullable enable
using System.Text.Json;
using DeskGoMapEntry = AIGuiders.Platform.Modeling.Cockpit.Cds.DeskGoMapEntry;

namespace CdpMcp.Cockpit.Cds;

/// <summary>CDS CCU: go-verb allowlist → organ tool + default args (ADR 0036).</summary>
public sealed partial class DeskGoMapCatalog : ICockpitComputeUnit
{
    static readonly IReadOnlyDictionary<string, JsonElement> NoDefaults =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, DeskGoMapEntry> Map => BuiltIns;

    public bool Contains(string verb) => BuiltIns.ContainsKey(verb);

    public bool TryGet(string verb, out DeskGoMapEntry entry) => BuiltIns.TryGetValue(verb, out entry);

    public IEnumerable<string> Keys => BuiltIns.Keys;

    static Dictionary<string, JsonElement> Dict(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
            d[k] = JsonSerializer.SerializeToElement(v);
        return d;
    }
}
