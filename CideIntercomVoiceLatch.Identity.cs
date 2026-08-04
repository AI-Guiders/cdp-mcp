#nullable enable
namespace CdpMcp;

/// <summary>
/// Intercom speaker identity for Glass RoleLabel — personal name + kind,
/// not model id / raw origin=agent|human.
/// kind: guest (Cursor/external PF) · citizen (in-habitat FM peer) · operator (PM).
/// </summary>
internal static partial class CideIntercomVoiceLatch
{
    public const string KindGuest = "guest";
    public const string KindCitizen = "citizen";
    public const string KindOperator = "operator";

    /// <summary>Bootstrap guest label until sticky claim — not a forced passport.</summary>
    public const string DefaultNameGuest = "Кир";
    public const string DefaultNameCitizen = "Citizen";
    /// <summary>Generic operator bootstrap — personal names live in sticky identity latch (not repo default).</summary>
    public const string DefaultNameOperator = "Operator";

    public static string? NormalizeKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var k = raw.Trim().ToLowerInvariant();
        return k switch
        {
            "guest" or "cursor" or "external" => KindGuest,
            "citizen" or "fm" or "peer" => KindCitizen,
            // "who" is Agent Who (agent identity) — never an operator alias
            "operator" or "human" or "pm" => KindOperator,
            _ => null
        };
    }

    /// <summary>
    /// Fill name+kind: explicit arg → sticky seat identity → bootstrap default.
    /// Sticky = agent-line continuity (freeform Who / nick); change anytime via claim or send name=.
    /// </summary>
    public static (string Name, string Kind) ResolveIdentity(
        string fromSeat,
        string origin,
        string? name,
        string? kind)
    {
        var k = NormalizeKind(kind);
        if (k is null)
        {
            if (string.Equals(origin, OriginHuman, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fromSeat, SeatPm, StringComparison.OrdinalIgnoreCase))
                k = KindOperator;
            else
                k = KindGuest;
        }

        var n = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (n is null)
        {
            var sticky = CideIntercomIdentityLatch.TrySeat(fromSeat);
            if (sticky is not null && !string.IsNullOrWhiteSpace(sticky.Name))
            {
                n = sticky.Name.Trim();
                if (NormalizeKind(sticky.Kind) is { } stickyKind)
                    k = stickyKind;
            }
        }

        n ??= k switch
        {
            KindOperator => DefaultNameOperator,
            KindCitizen => DefaultNameCitizen,
            _ => DefaultNameGuest
        };

        return (n, k);
    }

    /// <summary>Glass RoleLabel / journal: <c>Кир · guest @PF → @PM</c>.</summary>
    public static string FormatRoleLabel(
        string fromSeat,
        string toSeat,
        string name,
        string kind)
    {
        var from = "@" + fromSeat.Trim().ToUpperInvariant();
        var to = "@" + toSeat.Trim().ToUpperInvariant();
        return $"{name} · {kind} {from} → {to}";
    }
}
