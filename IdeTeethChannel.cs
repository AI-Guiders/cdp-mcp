#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=teeth</c> / Meta <c>cdp_teeth</c> — one-glance guest-host environment.
/// Afferent timeline for OOM tooth, CDT, remount/oom wake, partner away/here (ADR-0029).
/// </summary>
internal static class IdeTeethChannel
{
    public const string SchemaVersion = "teeth_channel/v1";
    public const string ToolName = "cdp_teeth";
    public const string GoName = "teeth";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = session;
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "help" or "status" => Scene(args),
            "tail" or "list" or "recent" => Tail(args),
            "explain" or "why" => Explain(args),
            _ => Fail("unknown_op", "op=scene|tail|explain")
        };
    }

    public static string PulseLine()
    {
        try
        {
            return BuildPulse(BuildNow(cdtLive: false));
        }
        catch
        {
            return "teeth · ? · go=teeth";
        }
    }

    static object Scene(IReadOnlyDictionary<string, JsonElement> args)
    {
        var cdtLive = OptBool(args, "cdt") ?? false;
        var now = BuildNow(cdtLive);
        var last = IdeTeethTape.ReadTail(12);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = BuildPulse(now),
            now = NowDto(now),
            last = last.Select(IdeTeethTape.Slim).ToArray(),
            explain = BuildExplain(now, last),
            tape = IdeTeethTape.TapePath,
            next = new object[]
            {
                new { go = GoName, label = "Tail", why = "op=tail limit=40" },
                new { go = GoName, label = "Explain", why = "op=explain — last wake/busy/away" },
                new { go = "ignite_desk", label = "Ignite list", why = "op=list arms" },
                new { go = "health", label = "Health", why = "teeth_pulse on cdp_health" }
            },
            hint =
                "Guest-host teeth: CDT/Stop, remount·oom delivery, OOM tooth, partner away/here. " +
                "First away = status; still away after ~60s → autonomy. cdt=true for live sample."
        };
    }

    static object Tail(IReadOnlyDictionary<string, JsonElement> args)
    {
        var limit = OptInt(args, "limit") ?? 40;
        var events = IdeTeethTape.ReadTail(limit);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "tail",
            go = GoName,
            count = events.Count,
            events = events.Select(IdeTeethTape.Slim).ToArray(),
            tape = IdeTeethTape.TapePath,
            hint = "Newest at end. Prefer op=scene for pulse+now+explain."
        };
    }

    static object Explain(IReadOnlyDictionary<string, JsonElement> args)
    {
        var armId = Opt(args, "id") ?? Opt(args, "arm");
        var now = BuildNow(cdtLive: false);
        var last = IdeTeethTape.ReadTail(40);
        var focused = string.IsNullOrWhiteSpace(armId)
            ? last.LastOrDefault(e =>
                e.Kind.StartsWith("wake_", StringComparison.OrdinalIgnoreCase)
                || e.Kind is "partner_away" or "partner_here" or "partner_away_escalate"
                || e.Kind is "oom_dialog" or "cdt_edge" or "deploy_hard")
            : last.LastOrDefault(e => string.Equals(e.ArmId, armId, StringComparison.OrdinalIgnoreCase));
        var text = focused is null
            ? BuildExplain(now, last)
            : ExplainEvent(focused, now);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "explain",
            go = GoName,
            explain = text,
            focus = focused is null ? null : IdeTeethTape.Slim(focused),
            now = NowDto(now),
            pulse = BuildPulse(now)
        };
    }

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

    static void TrySampleCdt()
    {
        try
        {
            var sample = IdeIgniteChannel.TrySampleComposerAsync(
                IdeIgniteChannel.DefaultPort, CancellationToken.None).GetAwaiter().GetResult();
            if (!sample.Ok)
            {
                IdeTeethTape.NoteGuest(sample.Kind == "down" ? null : sample.Kind, cdtUp: false);
                return;
            }

            IdeTeethTape.NoteGuest(sample.Kind, cdtUp: true);
        }
        catch
        {
            IdeTeethTape.NoteGuest(null, cdtUp: false);
        }
    }

    static string PartnerFromHild()
    {
        try
        {
            // AwayLatched = partner away; cleared on human Composer text.
            return IdeIgniteArmHost.HildDetectorForTests.AwayLatched ? "away" : "here";
        }
        catch
        {
            return "?";
        }
    }

    static string? TryHildPulse(object hild)
    {
        try
        {
            var json = JsonSerializer.Serialize(hild);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("pulse", out var p) ? p.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    static string? TryLiveVersion()
    {
        try
        {
            var asm = typeof(IdeTeethChannel).Assembly.GetName().Version;
            return asm is null ? null : $"{asm.Major}.{asm.Minor}.{asm.Build}";
        }
        catch
        {
            return null;
        }
    }

    static object NowDto(NowSnap now) => new
    {
        cdt_up = now.CdtUp,
        submit_kind = now.SubmitKind,
        remount_pending = now.RemountPending,
        oom_watch = now.OomWatch,
        oom_clicks = now.OomClicks,
        oom_wake_scheduled = now.OomWakeScheduled,
        live_version = now.LiveVersion,
        partner = now.Partner,
        autonomous = now.Autonomous,
        hild_pulse = now.HildPulse,
        arms = now.Arms.Select(a => new
        {
            id = a.Id,
            status = a.Status,
            reason = a.Reason,
            charge_mode = a.ChargeMode,
            task = a.Task,
            send_ok = a.SendOk,
            send_error = a.SendError,
            verdict = a.Verdict
        }).ToArray()
    };

    static string ShortArm(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "(arm)";
        var s = id.Trim();
        return s.Length <= 28 ? s : s[..28] + "…";
    }

    static object Fail(string error, string hint) => new
    {
        schema = SchemaVersion,
        ok = false,
        go = GoName,
        tool = ToolName,
        error,
        hint
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => null
        };
    }

    static bool? OptBool(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => null
        };
    }

    internal readonly record struct ArmRow(
        string Id,
        string Status,
        string? Reason,
        string? ChargeMode,
        string? Task,
        bool? SendOk,
        string? SendError,
        DateTimeOffset? SendInvokedUtc,
        string Verdict);

    internal readonly record struct NowSnap(
        bool? CdtUp,
        string? SubmitKind,
        bool RemountPending,
        bool OomWatch,
        int OomClicks,
        int OomWakeScheduled,
        string? LiveVersion,
        string Partner,
        bool Autonomous,
        ArmRow[] Arms,
        string? HildPulse);
}
