#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: merge GoMap keys with desk soft-organ aliases for desk_detail=nav.</summary>
public sealed class GoVerbsCatalogUnit : ICockpitComputeUnit
{
    public static readonly string[] ExtraDeskVerbs =
    [
        "quality", "gates", "sys", "chk", "ecl", "qrh", "eqrh", "review", "nav",
        "tiles", "layout", "tile", "seats", "seat", "repl", "ccl", "tasks", "plan",
        "feature", "task", "promote", "share", "confirm", "reject", "report", "evidence",
        "alert", "eicas", "sa", "pressure", "pressure_desk", "compact_prep", "pre_compact",
        "problems", "problem", "errlist", "errorlist", "err", "diags", "plugins", "plugin", "vsix"
    ];

    public string[] Merge(IEnumerable<string> primaryKeys, IEnumerable<string>? extra = null) =>
        primaryKeys
            .Concat(extra ?? ExtraDeskVerbs)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
