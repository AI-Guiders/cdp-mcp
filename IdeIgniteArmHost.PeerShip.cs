#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    /// <summary>
    /// SoftFL densify (HIH): wake waiters when another Who ships a leaf.
    /// Timer ≠ peer ship — arm with when=peer_ship|leaf_done|ship.
    /// </summary>
    public static void NotifyPeerShip(string? pulse = null, string? detail = null) =>
        Notify("peer_ship", ok: true, pulse: pulse ?? "shipped", detail: detail);

    /// <summary>
    /// Intercom prose that means leaf ship (not Autoi Radio thrash).
    /// Strict: citizen|guest + explicit ship cue — CoT dumps alone do not fire.
    /// </summary>
    internal static bool LooksLikePeerShipSignal(string? body, string? kind, string? name)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;
        if (CideIntercomVoiceLatch.IsSystemVoiceWho(name))
            return false;

        var k = CideIntercomVoiceLatch.NormalizeKind(kind);
        if (k is not (CideIntercomVoiceLatch.KindCitizen or CideIntercomVoiceLatch.KindGuest))
            return false;

        var t = body.Trim();
        if (t.StartsWith("peer_ship:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("ship ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("shipped", StringComparison.OrdinalIgnoreCase))
            return true;

        if (t.Contains("SoftFL shipped", StringComparison.OrdinalIgnoreCase)
            || t.Contains("leaf shipped", StringComparison.OrdinalIgnoreCase)
            || t.Contains("peer ship", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
