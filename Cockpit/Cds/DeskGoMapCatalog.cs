#nullable enable
using System.Text.Json;
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Cds;

/// <summary>CDS CCU: go-verb allowlist → organ tool + default args (ADR 0036).</summary>
public sealed partial class DeskGoMapCatalog : ICockpitComputeUnit
{
    public readonly record struct Entry(string Tool, IReadOnlyDictionary<string, JsonElement>? Defaults);


    public IReadOnlyDictionary<string, Entry> Map => BuiltIns;

    public bool Contains(string verb) => BuiltIns.ContainsKey(verb);

    public bool TryGet(string verb, out Entry entry) => BuiltIns.TryGetValue(verb, out entry);

    public IEnumerable<string> Keys => BuiltIns.Keys;

    static Dictionary<string, JsonElement> Dict(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
            d[k] = JsonSerializer.SerializeToElement(v);
        return d;
    }
}
