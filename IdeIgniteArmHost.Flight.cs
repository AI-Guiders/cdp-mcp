#nullable enable

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

internal static partial class IdeIgniteArmHost
{
    static Func<ContinuityFlight>? FlightProbe;

    /// <summary>Bind TM-aware flight probe (handoff / no-act plateau).</summary>
    public static void BindFlightProbe(Func<ContinuityFlight> probe) => FlightProbe = probe;

    /// <summary>Test/compat: true = fly, false = no active task.</summary>
    public static void BindTaskFocus(Func<bool> probe) =>
        FlightProbe = () => probe() ? ContinuityFlight.Fly : ContinuityFlight.NoActiveTask;

    internal static ContinuityFlight ProbeFlight() => FlightProbe?.Invoke() ?? ContinuityFlight.Fly;

    internal static bool HasActiveTaskFocus() => ProbeFlight() != ContinuityFlight.NoActiveTask;

    internal static bool IsEpicClosed(ContinuityFlight flight) =>
        flight is ContinuityFlight.EpicClosedHandoff or ContinuityFlight.EpicClosedNoAct;

    internal static string EpicClosedReason(ContinuityFlight flight) => flight switch
    {
        ContinuityFlight.EpicClosedHandoff => "focus_handoff",
        ContinuityFlight.EpicClosedNoAct => "no_act_tasks",
        ContinuityFlight.NoActiveTask => "no_active_task",
        _ => "fly"
    };
}
