#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    /// <summary>Snapshot for wake tier strategies — built once per fire.</summary>
    internal sealed class WakePreflightContext
    {
        public bool Faulted { get; init; }
        public bool WorkspaceBound { get; init; }
        public int FeatureCount { get; init; }
        public Guid? WakeLeafId { get; init; }
        public string? LeafTitle { get; init; }
        public string? FeatureTitle { get; init; }
        public bool LeafFocused { get; init; }
        public bool HotStashPresent { get; init; }

        public static WakePreflightContext Capture()
        {
            try
            {
                if (!IdeStageCycle.TryWorkspace(out var store, out var state, out _))
                    return new WakePreflightContext
                    {
                        HotStashPresent = IdePressureChannel.HasRecallSsotForWake(),
                    };

                var snap = store.TaskManagerSnapshot(state);
                var leafId = IdeIgniteArmHost.ResolveWakeLeafId(store, state);
                var title = leafId is { } id ? store.StageTitle(state, id)?.Trim() : null;
                var feature = snap.ActiveFeatureTitle?.Trim();
                if (string.IsNullOrWhiteSpace(feature))
                    feature = "—";

                return new WakePreflightContext
                {
                    WorkspaceBound = true,
                    FeatureCount = snap.Features.Count,
                    WakeLeafId = leafId,
                    LeafTitle = title,
                    FeatureTitle = feature,
                    LeafFocused = leafId is not null && state.ActiveStageId == leafId,
                    HotStashPresent = IdePressureChannel.HasRecallSsotForWake(),
                };
            }
            catch
            {
                return new WakePreflightContext
                {
                    Faulted = true,
                    HotStashPresent = IdePressureChannel.HasRecallSsotForWake(),
                };
            }
        }
    }
}
