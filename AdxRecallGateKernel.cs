#nullable enable

namespace CdpMcp;

/// <summary>
/// ADR-0024 recall-gate ceremony as a pure kernel (ADX assertion ADX-RG-001).
/// Runtime: cheap bool checks. Tests: Microsoft.Z3 proves the same relation.
/// </summary>
internal static class AdxRecallGateKernel
{
    public enum Gate
    {
        None = 0,
        Pull = 1,
        Reconcile = 2,
        Align = 3,
        Ready = 4
    }

    public static Gate Parse(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "pull" or "recall_pull" or "1" => Gate.Pull,
        "reconcile" or "recon" or "steer" or "2" => Gate.Reconcile,
        "align" or "aligned" or "3" => Gate.Align,
        "ready" or "ok" or "green" or "4" => Gate.Ready,
        _ => Gate.None
    };

    /// <summary>Single-step legality under ADR-0024 (+ SSOT / strict shortcuts).</summary>
    public static bool IsAllowed(Gate from, Gate to, bool ssot, bool strictRecall)
    {
        if (to == Gate.None)
            return false;

        // strict recall always lands on pull
        if (strictRecall && to == Gate.Pull)
            return true;

        // SSOT shortcut → ready (from any, including none)
        if (ssot && to == Gate.Ready)
            return true;

        // cold recall without SSOT → pull
        if (from == Gate.None && to == Gate.Pull)
            return true;

        return IsCeremonyStep(from, to);
    }

    public static bool IsCeremonyStep(Gate from, Gate to) => (from, to) switch
    {
        (Gate.Pull, Gate.Reconcile) => true,
        (Gate.Reconcile, Gate.Align) => true,
        (Gate.Align, Gate.Ready) => true,
        _ => false
    };

    /// <summary>
    /// Forbidden: pull→ready in one step without SSOT (the classic ceremony skip).
    /// </summary>
    public static bool IsForbiddenSkip(Gate from, Gate to, bool ssot) =>
        from == Gate.Pull && to == Gate.Ready && !ssot;

    public static object CheckCard(Gate from, Gate to, bool ssot, bool strictRecall)
    {
        var ok = IsAllowed(from, to, ssot, strictRecall);
        var forbid = IsForbiddenSkip(from, to, ssot);
        return new
        {
            id = "ADX-RG-001",
            ok = ok && !forbid,
            from = from.ToString().ToLowerInvariant(),
            to = to.ToString().ToLowerInvariant(),
            ssot,
            strict_recall = strictRecall,
            ceremony = IsCeremonyStep(from, to),
            forbidden_skip = forbid,
            pulse = ok && !forbid ? "recall_gate ok" : "recall_gate FAIL"
        };
    }
}
