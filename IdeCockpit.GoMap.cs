#nullable enable
using CdpMcp.Cockpit.Cds;

namespace CdpMcp;

/// <summary>
/// CDS go-verb allowlist peel — delegates to <see cref="DeskGoMapCatalog"/> (ADR 0036).
/// </summary>
internal static partial class IdeCockpit
{
    static readonly DeskGoMapCatalog DeskGoMaps = new();

    /// <summary>Allowlist desk verbs → organ tools. Cockpit stays a пульт, not the organ.</summary>
    static IReadOnlyDictionary<string, DeskGoMapCatalog.Entry> GoMap => DeskGoMaps.Map;
}
