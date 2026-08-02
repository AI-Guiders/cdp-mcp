#nullable enable

namespace CdpMcp;

/// <summary>
/// LeafPlateau plants <c>autonomous-seed-wake</c> for empty-board continuity.
/// Fire-time recheck: if an incomplete TM leaf already landed mid-window,
/// do not Guest-Autoi CDT inject "seed next leaf" — redirect to <c>leaf-wake</c>.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    static Func<string?>? IncompleteLeafTitleProbe;

    /// <summary>Tests: force incomplete-leaf title without WitDB. null = live peek.</summary>
    internal static void BindIncompleteLeafTitleProbe(Func<string?>? probe) =>
        IncompleteLeafTitleProbe = probe;

    /// <summary>
    /// Before habitat/CDT delivery of autonomous seed: suppress when board already has work.
    /// Returns true when fire must stop (seed removed; leaf-wake may have been armed).
    /// </summary>
    internal static bool TrySuppressAutonomousSeedBeforeDelivery(IgniteArm arm)
    {
        if (!arm.Id.Equals(AutonomousSeedArmId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryResolveIncompleteLeafTitle(out var title))
            return false;

        IdeFlightDataRecorder.RecordWake(
            "wake_suppress", arm.Id, ToolFromWakeArm(arm), "board_has_incomplete_leaf");
        Remove(arm.Id);

        if (!IsLeafWakeLive())
            _ = ArmForLeaf(title, "autonomous_seed_board_recheck");

        return true;
    }

    /// <summary>Test/helper: suppress live seed arm if present.</summary>
    internal static bool TrySuppressLiveAutonomousSeedBeforeDelivery()
    {
        IgniteArm? seed;
        lock (Gate)
        {
            seed = Arms.FirstOrDefault(a =>
                a.Id.Equals(AutonomousSeedArmId, StringComparison.OrdinalIgnoreCase));
        }

        return seed is not null && TrySuppressAutonomousSeedBeforeDelivery(seed);
    }

    static bool IsLeafWakeLive()
    {
        lock (Gate)
        {
            return Arms.Any(a =>
                a.Id.Equals(LeafWakeArmId, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");
        }
    }

    static bool TryResolveIncompleteLeafTitle(out string title)
    {
        title = "";
        try
        {
            if (IncompleteLeafTitleProbe is { } probe)
            {
                var t = probe();
                if (string.IsNullOrWhiteSpace(t))
                    return false;
                title = t.Trim();
                return true;
            }

            if (!IdeStageCycle.TryWorkspace(out var store, out var state, out _))
                return false;

            var id = store.FindFirstIncompleteLeaf(state);
            if (id is null)
                return false;

            var leafTitle = store.StageTitle(state, id.Value);
            if (string.IsNullOrWhiteSpace(leafTitle))
                return false;

            title = leafTitle.Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
