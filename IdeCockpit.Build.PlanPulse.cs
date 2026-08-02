#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Desk-pulse fast path for BuildAsync — skip upfront git / quality / full SoftOrgan seat resolve.
/// <c>go_detail=full</c> = organ depth only. <c>seats_detail=full</c> alone stays on pulse
/// (W-spray refused early — same as SeatsDetailGateUnit). <c>pane_full=</c> stays on pulse and
/// resolves one matched seat only. CDP-ADR-0020: deferred soft organs skip glass spray; organ-only skip nav.
/// Plan stays a special-case of this path.
/// </summary>
internal static partial class IdeCockpit
{
    /// <summary>True when cockpit should return a slim desk-pulse instead of full BuildAsync spray.</summary>
    /// <summary>True when cockpit should return a slim desk-pulse instead of full BuildAsync spray.</summary>
    public static bool WantsDeskPulseFastPath(IReadOnlyDictionary<string, JsonElement> args)
    {
        // Desk stays pulse always (ADR-0020). go_detail=full = organ depth; seats_detail=full alone
        // thrash-refuses; pane_full= resolves one seat on pulse (not TryGitAsync / all-seat spray).
        _ = args;
        return true;
    }

    /// <summary>Plan-only gate (tests + legacy). Desk pulse + organ pin is plan.</summary>
    public static bool WantsPlanPulseFastPath(
        string? goVerb,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (goVerb is not { Length: > 0 })
            return false;
        if (CanonicalOrganPin(goVerb) is not "plan")
            return false;
        return WantsDeskPulseFastPath(args);
    }

    static bool AnyDeferredSoftWant(DeferredSoftWants w) =>
        w.Sys || w.Chk || w.Qrh || w.Alert || w.Problems || w.Plugins || w.Review;

    /// <summary>Thrash when seats_detail=full without pane_full (early refuse on pulse path).</summary>
    static string? DeskPulseWSprayThrash(IReadOnlyDictionary<string, JsonElement> args)
    {
        var gate = SeatsDetailGate.Compute(new SeatsDetailGateUnit.Input(
            SeatsDetailRaw: OptString(args, "seats_detail") ?? OptString(args, "view_detail"),
            FullPane: OptString(args, "pane_full") ?? OptString(args, "full_pane"),
            SeatsPanesFlag: BoolOr(args, "seats_panes", false),
            CompactDefaultTrue: BoolOr(args, "compact", true)));
        return gate.ThrashNote;
    }

}