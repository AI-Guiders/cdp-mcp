#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=teeth</c> / Meta <c>cdp_teeth</c> — one-glance guest-host environment.
/// Afferent timeline for OOM tooth, CDT, remount/oom wake, partner away/here (ADR-0029).
/// </summary>
internal static partial class IdeTeethChannel
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
