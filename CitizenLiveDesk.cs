#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Live desk A for citizen turns — seat map + TM pulse without guest pasting <c>board=</c>.
/// </summary>
internal static class CitizenLiveDesk
{
    public readonly record struct Afferent(string[] BoardLines, string? TmPulse, bool FromLive);

    /// <summary>Pure pack from seat map + optional TM pulse (testable).</summary>
    public static Afferent FromSeats(
        IReadOnlyDictionary<string, string?> seatMap,
        string? tmPulse)
    {
        var lines = new List<string>(IdeDeskSeats.Order.Length);
        foreach (var seatId in IdeDeskSeats.Order)
        {
            seatMap.TryGetValue(seatId, out var organ);
            var empty = string.IsNullOrWhiteSpace(organ);
            var line = empty
                ? "(empty)"
                : PlanLine(organ!, tmPulse);
            lines.Add(IdeDeskView.FormatBoardLine(
                new IdeDeskView.Slot(seatId, empty ? null : organ, empty, true, line, false)));
        }

        return new Afferent(lines.ToArray(), string.IsNullOrWhiteSpace(tmPulse) ? null : tmPulse.Trim(), FromLive: true);
    }

    /// <summary>Snapshot seats + live TM (WitDB via <see cref="IdeStageCycle"/>, else plan latch).</summary>
    public static Afferent TryCaptureLive()
    {
        try
        {
            IdeDeskSeats.EnsureDefaultsFromSettings();
            var map = IdeDeskSeats.Snapshot();
            var tm = TryLiveTmPulse();
            return FromSeats(map, tm);
        }
        catch
        {
            return new Afferent([], null, FromLive: false);
        }
    }

    public static string? TryLiveTmPulse()
    {
        try
        {
            if (IdeStageCycle.TryWorkspace(out var store, out var state, out var phase))
            {
                var pulse = IdeTaskManager.PulseLine(store, state, phase);
                if (!string.IsNullOrWhiteSpace(pulse) && !pulse.Equals("no plan", StringComparison.OrdinalIgnoreCase))
                    return pulse;
            }

            var latch = CidePlanLatch.TryRead();
            if (latch is { Active: true } && !string.IsNullOrWhiteSpace(latch.Pulse))
                return latch.Pulse.Trim();
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string PlanLine(string organ, string? tmPulse)
    {
        var pin = IdeDeskView.ShortOrgan(organ);
        if (!string.IsNullOrWhiteSpace(tmPulse)
            && pin.Equals("plan", StringComparison.OrdinalIgnoreCase))
            return IdeDeskView.HumanizePulse(tmPulse, organ);
        return pin;
    }
}
