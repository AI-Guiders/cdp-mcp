#nullable enable
using static CdpMcp.IdeIgniteArmHost;

namespace CdpMcp;

/// <summary>Solo-flight continuity gate — last_once must not invent the next epic.</summary>
internal enum ContinuityFlight
{
    /// <summary>OK to arm last_once continuity.</summary>
    Fly,
    /// <summary>No TM focus stage.</summary>
    NoActiveTask,
    /// <summary>Focus task is @handoff — epic closed, await operator.</summary>
    EpicClosedHandoff,
    /// <summary>Feature has no open non-handoff work left.</summary>
    EpicClosedNoAct
}

internal sealed partial class CdpIgniteArmHost
{
    Func<ContinuityFlight>? FlightProbe;

    /// <summary>Bind TM-aware flight probe (handoff / no-act plateau).</summary>
    public void BindFlightProbe(Func<ContinuityFlight> probe) => FlightProbe = probe;

    Action? CitizenFocusLaneBind;

    /// <summary>prefer_citizen wake → switch TM FocusLane to Face Who (tip≠Face).</summary>
    public void BindCitizenFocusLane(Action bind) => CitizenFocusLaneBind = bind;

    /// <summary>Best-effort: Face lane on citizen Autoi consume. Never throws into fire path.</summary>
    internal void TryApplyCitizenFocusLane()
    {
        try
        {
            CitizenFocusLaneBind?.Invoke();
        }
        catch
        {
            /* workspace optional at cold fire */
        }
    }


    /// <summary>Test/compat: true = fly, false = no active task.</summary>
    public void BindTaskFocus(Func<bool> probe) =>
        FlightProbe = () => probe() ? ContinuityFlight.Fly : ContinuityFlight.NoActiveTask;

    internal ContinuityFlight ProbeFlight() => FlightProbe?.Invoke() ?? ContinuityFlight.Fly;

    internal bool HasActiveTaskFocus() => ProbeFlight() != ContinuityFlight.NoActiveTask;

    internal bool IsEpicClosed(ContinuityFlight flight) =>
        flight is ContinuityFlight.EpicClosedHandoff or ContinuityFlight.EpicClosedNoAct;

    internal string EpicClosedReason(ContinuityFlight flight) => flight switch
    {
        ContinuityFlight.EpicClosedHandoff => "focus_handoff",
        ContinuityFlight.EpicClosedNoAct => "no_act_tasks",
        ContinuityFlight.NoActiveTask => "no_active_task",
        _ => "fly"
    };
}
