#nullable enable
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Phase → desk layout (SA max). Session phase is SSOT; Stage.PhaseAffinity is soft.
/// Auto-apply on <c>cdp_context</c> phase change unless <c>desk.layout.hold</c> / <c>layout_hold</c>.
/// </summary>
internal static class IdePhaseLayout
{
    /// <summary>Full P|F|M presets — phase change replaces sticky (hold to skip).</summary>
    public static string LayoutIdFor(CdpPhase phase) => phase switch
    {
        CdpPhase.Recall => "agent",
        CdpPhase.Explore => "phase-explore",
        CdpPhase.Clarify => "phase-explore",
        CdpPhase.Plan => "agent",
        CdpPhase.Act => "bug",
        CdpPhase.Verify => "verify",
        CdpPhase.Review => "phase-review",
        CdpPhase.Handoff => "phase-handoff",
        _ => "phase-explore"
    };

    public static bool IsHold(IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args = null)
    {
        if (args is not null
            && args.TryGetValue("layout_hold", out var el)
            && el.ValueKind is System.Text.Json.JsonValueKind.True)
            return true;
        if (args is not null
            && args.TryGetValue("layout_hold", out var s)
            && s.ValueKind == System.Text.Json.JsonValueKind.String
            && bool.TryParse(s.GetString(), out var b)
            && b)
            return true;
        return IdeSettingsHabitat.EffectiveDeskLayoutHold();
    }

    /// <returns>Applied layout id, or null if held / unknown / seats off.</returns>
    public static string? TryApplyForPhase(CdpPhase phase, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args = null)
    {
        if (IsHold(args))
            return null;
        if (!IdeDeskSeats.IsSeatsMode())
            return null;
        var id = LayoutIdFor(phase);
        return IdeDeskSeats.TryApplyPreset(id, merge: false) ? id : null;
    }
}
