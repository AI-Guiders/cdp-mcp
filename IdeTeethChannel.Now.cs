#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>NowSnap builders + explain helpers for go=teeth.</summary>
internal static partial class IdeTeethChannel
{
    internal static NowSnap BuildNow(bool cdtLive)
    {
        if (cdtLive)
            TrySampleCdt();

        var armRows = IdeIgniteArmHost.Snapshot()
            .Where(a => a.Status is "armed" or "firing" or "error" or "awaiting")
            .Select(a => new ArmRow(
                a.Id,
                a.Status,
                a.Reason,
                a.ChargeMode,
                a.Task,
                a.SendOk,
                a.SendError,
                a.SendInvokedUtc,
                IdeIgniteArmHost.Verdict(a)))
            .ToArray();

        var hild = IdeIgniteArmHost.HildStatusPayload();
        var partner = PartnerFromHild();

        return new NowSnap(
            CdtUp: IdeTeethTape.LastCdtUp,
            SubmitKind: IdeTeethTape.LastSubmitKind,
            RemountPending: IdeRemountWake.HasPending(),
            OomWatch: IdeIgniteArmHost.IsOomWatchRunning,
            OomClicks: IdeIgniteArmHost.OomNewWindowClickCount,
            OomWakeScheduled: IdeIgniteArmHost.OomWakeScheduleCount,
            LiveVersion: TryLiveVersion(),
            Partner: partner,
            Autonomous: IdeIgniteArmHost.IsAutonomousArmed(),
            Arms: armRows,
            HildPulse: TryHildPulse(hild));
    }

    internal static string BuildPulse(NowSnap now)
    {
        var cdt = now.CdtUp is null ? "cdt=?" : now.CdtUp == true ? "cdt=up" : "cdt=down";
        if (!string.IsNullOrWhiteSpace(now.SubmitKind))
            cdt = $"{cdt}/{now.SubmitKind}";

        var partner = now.Partner switch
        {
            "away" => "partner=away",
            "here" => "partner=here",
            _ => "partner=?"
        };
        if (now.Autonomous)
            partner += "/auto";

        var wake = DescribeWakeArm(now);
        var oom = now.OomWatch ? "oom=watch" : "oom=off";
        if (now.OomClicks > 0)
            oom += $"#click={now.OomClicks}";
        var remount = now.RemountPending ? "remount=pending" : null;
        var live = string.IsNullOrWhiteSpace(now.LiveVersion) ? null : $"live={now.LiveVersion}";

        var parts = new List<string> { "teeth", cdt, partner };
        if (wake is not null) parts.Add(wake);
        if (remount is not null) parts.Add(remount);
        parts.Add(oom);
        if (live is not null) parts.Add(live);
        return string.Join(" · ", parts);
    }

    internal static string BuildExplain(NowSnap now, IReadOnlyList<IdeTeethTape.TeethEvent> last)
    {
        foreach (var a in now.Arms.Where(x => x.Status == "firing"))
        {
            if (a.SendInvokedUtc is not null && a.SendOk is null)
            {
                var submit = now.SubmitKind ?? "?";
                if (submit is "stop" or "queue")
                    return $"{ShortArm(a.Id)} firing — inject waiting; Composer submit={submit} (busy). Charge not in chat yet.";
                return $"{ShortArm(a.Id)} firing — send invoked, outcome pending (reason={a.Reason ?? "?"}).";
            }

            return $"{ShortArm(a.Id)} firing (reason={a.Reason ?? "?"}).";
        }

        if (string.Equals(now.Partner, "away", StringComparison.Ordinal)
            && !now.Autonomous)
            return "Partner away (HILD) — status only; still away after ~60s → autonomy.";

        if (now.Autonomous && string.Equals(now.Partner, "away", StringComparison.Ordinal))
            return "Partner away + autonomous armed — do not wait; continue leaves.";

        if (now.RemountPending)
            return "Remount pending on disk — next MCP boot schedules reason=remount wake.";

        var lastWake = last.LastOrDefault(e =>
            e.Kind is "wake_send" or "wake_schedule" or "wake_requeue" or "wake_drop"
                or "oom_dialog" or "cdt_edge" or "deploy_hard"
                or "partner_away" or "partner_here" or "partner_away_escalate");
        if (lastWake is not null)
            return ExplainEvent(lastWake, now);

        if (now.CdtUp == false)
            return "CDT down — guest host may be restarting; OOM tooth watches for recover.";

        return "Teeth quiet — no firing wake; partner here or unknown; CDT/oom watch idle.";
    }

    static string ExplainEvent(IdeTeethTape.TeethEvent e, NowSnap now)
    {
        if (e.Kind == "wake_send"
            && (string.Equals(e.Detail, "busy_timeout", StringComparison.OrdinalIgnoreCase)
                || e.SubmitKind is "stop" or "queue"))
            return $"{ShortArm(e.ArmId)} send busy — Composer {e.SubmitKind ?? "Stop/Queue"}; expect requeue then Voice.";

        if (e.Kind == "wake_send"
            && string.Equals(e.Detail, "ok", StringComparison.OrdinalIgnoreCase))
            return $"{ShortArm(e.ArmId)} send ok — charge should be in Composer (reason={e.Reason ?? "?"}).";

        return e.Kind switch
        {
            "wake_requeue" => $"{ShortArm(e.ArmId)} requeued ({e.Detail ?? "busy"}) — will fire again when due.",
            "wake_drop" => $"{ShortArm(e.ArmId)} dropped ({e.Detail ?? "reclaim"}) — wake may be lost.",
            "wake_schedule" => $"{ShortArm(e.ArmId)} scheduled reason={e.Reason ?? "?"}.",
            "wake_fire" => $"{ShortArm(e.ArmId)} entered firing (reason={e.Reason ?? "?"}).",
            "oom_dialog" => "OOM dialog tooth clicked Reopen — host recover in progress.",
            "cdt_edge" when string.Equals(e.Detail, "down", StringComparison.OrdinalIgnoreCase)
                => "CDT went down — guest host flake/OOM.",
            "cdt_edge" => $"CDT up after {(e.DownMs ?? 0)}ms down — oom-wake may schedule.",
            "deploy_hard" => "Hard deploy stamped remount pending.",
            "partner_away" => "Partner status → away (HILD). Brief step-out — wait ~60s before autonomy.",
            "partner_here" => "Partner status → here (human Composer text cleared away latch).",
            "partner_away_escalate" => "Still away after escalate window — autonomy armed (partner likely gone long).",
            _ => $"{e.Kind}: {e.Detail ?? e.Reason ?? "recorded"} (submit={now.SubmitKind ?? "?"})."
        };
    }

    static string? DescribeWakeArm(NowSnap now)
    {
        var arms = now.Arms
            .Where(a => a.Status is "armed" or "firing")
            .Where(a =>
                a.Id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                || a.Id.StartsWith(IdeOomWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Reason, "remount", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Reason, "oom", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Status == "firing" ? 0 : 1)
            .ToList();
        if (arms.Count == 0)
            return null;

        var a = arms[0];
        var tag = a.Reason
                  ?? (a.Id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                      ? "remount"
                      : a.Id.StartsWith(IdeOomWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                          ? "oom"
                          : "wake");
        if (a.Status == "firing" && a.SendInvokedUtc is not null && a.SendOk is null
            && now.SubmitKind is "stop" or "queue")
            return $"{tag}=firing/busy";
        if (a.Status == "firing")
            return $"{tag}=firing";
        return $"{tag}={a.Status}";
    }
}
