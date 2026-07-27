#nullable enable
namespace CdpMcp.Cockpit.Surface;

/// <summary>How seat pane projects a soft-organ board (Handle still peel).</summary>
public enum SoftOrganPresentMode
{
    /// <summary>wantFull → Full wrap; else board as-is.</summary>
    FullOr,

    /// <summary>wantFull → Full; else pulse card (schema/hint from meta).</summary>
    PulseLine,

    /// <summary>wantFull → Full; else pulse + result board.</summary>
    PulseWithResult
}
