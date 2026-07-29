#nullable enable
using System.Text.RegularExpressions;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>TM → Autoi flight probe: handoff / no-act = epic closed plateau.</summary>
internal static partial class IdeTaskManager
{
    static readonly Regex TitlePhaseTag = new(
        @"@(recall|explore|clarify|plan|act|verify|handoff)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Whether solo Autoi may invent more work, or must await the operator.</summary>
    public static ContinuityFlight ProbeContinuityFlight(
        IntentWorkspaceStore? store,
        IntentWorkspaceState state)
    {
        if (state.ActiveStageId is null)
            return ContinuityFlight.NoActiveTask;
        if (store is null)
            return ContinuityFlight.Fly;

        try
        {
            var snap = store.TaskManagerSnapshot(state);
            var focusPhase = ResolvePhaseWire(snap.ActiveStagePhaseAffinity, snap.ActiveStageTitle);
            if (IsHandoffPhase(focusPhase))
                return ContinuityFlight.EpicClosedHandoff;

            var feature = snap.Features.FirstOrDefault(f => f.IsActive);
            if (feature is null)
                return ContinuityFlight.NoActiveTask;

            var open = feature.Stages
                .Where(s => s.Status is "pending" or "active")
                .ToList();
            if (open.Count == 0)
                return ContinuityFlight.EpicClosedNoAct;

            if (open.All(s => IsHandoffPhase(ResolvePhaseWire(s.PhaseAffinity, s.Title))))
                return ContinuityFlight.EpicClosedHandoff;

            return ContinuityFlight.Fly;
        }
        catch
        {
            // Fail open — broken WitDB must not brick Autoi.
            return ContinuityFlight.Fly;
        }
    }

    internal static string? ResolvePhaseWire(string? phaseAffinity, string? title)
    {
        if (phaseAffinity is { Length: > 0 } aff)
            return aff.Trim().ToLowerInvariant();
        if (title is not { Length: > 0 })
            return null;
        var m = TitlePhaseTag.Match(title);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    static bool IsHandoffPhase(string? phase) =>
        phase is not null && phase.Equals("handoff", StringComparison.OrdinalIgnoreCase);
}
