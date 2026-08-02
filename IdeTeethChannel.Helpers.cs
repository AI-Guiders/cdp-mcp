#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeTeethChannel
{
    static void TrySampleCdt()
    {
        try
        {
            var sample = IdeIgniteChannel.TrySampleComposerAsync(IdeIgniteChannel.DefaultPort, CancellationToken.None).GetAwaiter().GetResult();
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
        arms = now.Arms.Select(a => new { id = a.Id, status = a.Status, reason = a.Reason, charge_mode = a.ChargeMode, task = a.Task, send_ok = a.SendOk, send_error = a.SendError, verdict = a.Verdict }).ToArray()
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
}