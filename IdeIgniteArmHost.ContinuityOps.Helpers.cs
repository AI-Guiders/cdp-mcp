#nullable enable
using System.Text.Json;

namespace CdpMcp;
internal static partial class IdeIgniteArmHost
{
    internal static object ContinuitySlice(IReadOnlyList<IgniteArm>? list = null)
    {
        list ??= Snapshot();
        var armed = list.Where(a => a.Status is "armed" or "firing").ToList();
        var awaiting = list.Where(a => a.Status == "awaiting").ToList();
        var providerBlocked = list.Where(a => a.Status == ProviderBlockedStatus).ToList();
        var errors = list.Count(a => a.Status == "error");
        var next = armed.Where(a => a.DueUtc is not null).OrderBy(a => a.DueUtc).Select(a => new { a.Id, a.Task, a.DueUtc, a.Status, a.Event }).FirstOrDefault();
        return new
        {
            armed = armed.Count,
            firing = list.Count(a => a.Status == "firing"),
            awaiting = awaiting.Count,
            provider_blocked = providerBlocked.Count > 0,
            new_thread_required = providerBlocked.Count > 0,
            error = errors,
            await_partner = awaiting.Count > 0,
            await_operator = awaiting.Count > 0, // legacy alias
            autonomous = IsAutonomousArmed(),
            await_arm = awaiting.Select(Slim).FirstOrDefault(),
            blocked_arm = providerBlocked.Select(Slim).FirstOrDefault(),
            next_due = next,
            tasks = armed.Select(a => a.Task).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList()
        };
    }

    internal static string ContinuityPulseLine(IReadOnlyList<IgniteArm>? list = null)
    {
        list ??= Snapshot();
        var blocked = list.Where(a => a.Status == ProviderBlockedStatus).ToList();
        if (blocked.Count > 0)
            return $"ignite · continuity · {ProviderBlockedStatus} · new_thread_required · latch={blocked.Count}";
        var awaiting = list.Count(a => a.Status == "awaiting");
        if (awaiting > 0)
            return $"ignite · continuity · awaiting_partner · latch={awaiting}";
        var auto = IsAutonomousArmed() ? " · autonomous" : "";
        var armed = list.Count(a => a.Status is "armed" or "firing");
        var err = list.Count(a => a.Status == "error");
        var next = list.Where(a => (a.Status is "armed" or "firing") && a.DueUtc is not null).OrderBy(a => a.DueUtc).FirstOrDefault();
        var due = next?.DueUtc is { } d ? $" · next {d:HH:mm:ss}Z" : "";
        var noise = err > 0 ? $" · stale={err}" : "";
        return $"ignite · continuity · armed={armed}{auto}{noise}{due}";
    }

    /// <summary>Mirror AutoIgnition continuity to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        var list = Snapshot();
        var armedCount = list.Count(a => a.Status is "armed" or "firing");
        var awaiting = list.Where(a => a.Status == "awaiting").ToList();
        var awaitingCount = awaiting.Count;
        var providerBlocked = list.Any(a => a.Status == ProviderBlockedStatus);
        // SoftInstrument chrome stays Dark Cockpit when no arms — HUD still gets autonomous/hild/course.
        var active = armedCount > 0 || awaitingCount > 0 || providerBlocked;
        var course = IdePressureChannel.TryPeekSealedCourse();
        var awaitPartner = awaitingCount > 0;
        var mode = !awaitPartner
            ? "fly"
            : awaiting.Any(a => string.Equals(a.Event, "halt", StringComparison.OrdinalIgnoreCase))
                ? "halt"
                : "talk";
        CideIgniteLatch.Publish(
            active,
            ContinuityPulseLine(list),
            armedCount,
            awaitingCount,
            providerBlocked,
            IsAutonomousArmed(),
            IsHildArmed(),
            course,
            awaitPartner: awaitPartner,
            mode: mode);
    }

    static IdeExplainability.ExplainCard ContinuityExplain(IReadOnlyList<IgniteArm> list)
    {
        var blocked = list.Where(a => a.Status == ProviderBlockedStatus).ToList();
        if (blocked.Count > 0)
            return IdeExplainability.New("ignite.continuity", ProviderBlockedStatus, $"continuity latch count={blocked.Count} requires a new chat", "open new chat");
        var awaiting = list.Where(a => a.Status == "awaiting").ToList();
        if (awaiting.Count > 0)
            return IdeExplainability.New("ignite.continuity", "awaiting_partner", $"last_once latch count={awaiting.Count} is waiting for partner acknowledgement", "cdp_ignite op=resume");
        var armed = list.Where(a => a.Status is "armed" or "firing").OrderBy(a => a.DueUtc ?? DateTimeOffset.MaxValue).ToList();
        if (armed.Count > 0)
        {
            var next = armed[0];
            var reason = next.Event == "timer" ? "timer_wait" : $"{next.Event}_wait";
            var authority = next.DueUtc is { } due ? $"continuity is armed and next due is {due:HH:mm:ss}Z" : "continuity is armed and waiting for its event";
            var nextStep = ContinuityArmedNextStep(IsAutonomousArmed());
            return IdeExplainability.New("ignite.continuity", reason, authority, nextStep);
        }

        return IdeExplainability.New("ignite.continuity", "idle", "no armed or latched continuity remains", "arm if continuity is needed");
    }

    /// <summary>Scene continuity tip — under autonomous do not teach wait-for-event park.</summary>
    internal static string ContinuityArmedNextStep(bool autonomous) =>
        autonomous
            ? "keep flying started TM leaf; timer is insurance — do not park"
            : "wait for event";

    static object? ExplainCardObject(IdeExplainability.ExplainCard? explain) => explain is null ? null : new
    {
        source = explain.Source,
        reason = explain.Reason,
        authority = explain.Authority,
        next_step = explain.NextStep,
        why = explain.WhyLine
    };
    /// <summary>Drop non-requeueable error arms + once stuck-firing with SendOk=true. Requeueable errors (busy_timeout/click_failed/…) revive → armed. Returns removed ids.</summary>
    static List<string> SweepNoiseUnlocked(bool persist)
    {
        List<string> removed;
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            var requeued = 0;
            foreach (var a in Arms.Where(x => x.Status == "error").ToList())
            {
                if (!TryReviveRequeueableErrorUnlocked(a, now, BusyBackoff(a.WaitSeconds), "hygiene_requeue_"))
                    continue;
                requeued++;
            }

            removed = Arms.Where(a => a.Status == "error" || (a.Once && a.Status == "firing" && a.SendOk == true)).Select(a => a.Id).ToList();
            if (removed.Count == 0 && requeued == 0)
                return removed;
            var set = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
            Arms.RemoveAll(a => set.Contains(a.Id));
            foreach (var id in removed)
                CancelInFlightFire(id);
            if (persist && (removed.Count > 0 || requeued > 0))
                PersistUnlocked();
        }

        return removed;
    }
}