#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Honest Cursor-bridge delivery evidence (temporary adapter, not harness SSOT).
/// States: armed → firing → send_ok|send_fail → transcript_observed|not_observed.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    public const string DeliveryNeedle = IdeIgniteChannel.CanonicalComposerCharge;

    public static object Delivery(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureLoaded();
        var arm = ResolveDeliveryArm(args);
        if (arm is null)
        {
            return new
            {
                schema = IdeIgniteChannel.Schema,
                ok = false,
                op = "delivery",
                error = "no_arm",
                hint = "arm first, or pass id= of awaiting/fired arm",
                go = IdeIgniteChannel.GoName,
                tool = IdeIgniteChannel.ToolName
            };
        }

        var observe = OptBool(args, "observe") ?? true;
        if (observe)
            TryObserveTranscript(arm, args);

        var verdict = Verdict(arm);
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "delivery",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = $"ignite · delivery · {verdict}",
            verdict,
            arm = Slim(arm),
            delivery = DeliveryDto(arm),
            bridge = "cursor_autoignition_temporary",
            hint = HintFor(verdict)
        };
    }

    public static object Watchdog(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureLoaded();
        var arm = ResolveDeliveryArm(args);
        if (arm is null)
        {
            return new
            {
                schema = IdeIgniteChannel.Schema,
                ok = false,
                op = "watchdog",
                error = "no_arm",
                hint = "need fired/awaiting arm id= or latch",
                go = IdeIgniteChannel.GoName,
                tool = IdeIgniteChannel.ToolName
            };
        }

        var hit = TryObserveTranscript(arm, args);
        var verdict = Verdict(arm);
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "watchdog",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = hit.Observed
                ? $"ignite · watchdog · transcript_observed · {Path.GetFileName(hit.Path)}"
                : "ignite · watchdog · not_observed",
            verdict,
            observed = hit.Observed,
            transcript_path = hit.Path,
            scanned = hit.Scanned,
            needle = DeliveryNeedle,
            arm_id = arm.Id,
            fired_utc = arm.FiredUtc,
            delivery = DeliveryDto(arm),
            bridge = "cursor_autoignition_temporary",
            hint = hit.Observed
                ? "Proof of delivery in Cursor transcript (diagnostic only — not harness ack)."
                : "Send may have happened without transcript observe yet — latency or wrong project folder."
        };
    }

    internal static string Verdict(IgniteArm a)
    {
        if (a.TranscriptObservedUtc is not null)
            return "transcript_observed";
        if (a.SendOk == false)
            return "send_fail";
        if (a.SendOk == true || a.Status is "awaiting" && a.FiredUtc is not null)
            return "not_observed";
        if (a.Status == "firing")
            return "firing";
        if (a.Status == "armed")
            return "armed";
        if (a.Status is "error" or ProviderBlockedStatus)
            return a.Status;
        return a.Status;
    }

    static object DeliveryDto(IgniteArm a) => new
    {
        armed_utc = a.CreatedUtc,
        firing_utc = a.Status == "firing" ? a.FiredUtc : null,
        send_invoked_utc = a.SendInvokedUtc,
        send_ok = a.SendOk,
        send_error = a.SendError,
        fired_utc = a.FiredUtc,
        transcript_observed_utc = a.TranscriptObservedUtc,
        transcript_path = a.TranscriptPath,
        verdict = Verdict(a)
    };

    static string HintFor(string verdict) => verdict switch
    {
        "transcript_observed" => "Bridge delivery confirmed in transcript — still temporary Cursor adapter.",
        "not_observed" => "CDT send reported ok (or awaiting) but transcript needle not seen — run op=watchdog.",
        "send_fail" => "Fire/send failed — see send_error / last_error.",
        "armed" => "Waiting for event/timer fire.",
        "firing" => "Inject in flight.",
        _ => "op=delivery|watchdog — honest bridge diagnostics."
    };

    static IgniteArm? ResolveDeliveryArm(IReadOnlyDictionary<string, JsonElement> args)
    {
        lock (Gate)
        {
            var id = Opt(args, "id") ?? Opt(args, "arm_id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return Arms.FirstOrDefault(a =>
                    a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            }

            return Arms
                .Where(a => a.Status is "awaiting" or "error" or ProviderBlockedStatus or "firing"
                    || a.FiredUtc is not null || a.SendInvokedUtc is not null)
                .OrderByDescending(a => a.FiredUtc ?? a.SendInvokedUtc ?? a.CreatedUtc)
                .FirstOrDefault();
        }
    }

    static (bool Observed, string? Path, int Scanned) TryObserveTranscript(
        IgniteArm armLiveOrClone,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        IgniteArm? live;
        lock (Gate)
        {
            live = Arms.FirstOrDefault(a =>
                a.Id.Equals(armLiveOrClone.Id, StringComparison.OrdinalIgnoreCase));
        }

        live ??= armLiveOrClone;
        if (live.TranscriptObservedUtc is not null)
            return (true, live.TranscriptPath, 0);

        var after = live.FiredUtc ?? live.SendInvokedUtc ?? live.CreatedUtc;
        var rootOverride = Opt(args, "transcript_root") ?? Opt(args, "root");
        var hit = ScanTranscriptsForNeedle(DeliveryNeedle, after, rootOverride, maxFiles: 40);
        if (!hit.Observed)
            return hit;

        lock (Gate)
        {
            var a = Arms.FirstOrDefault(x =>
                x.Id.Equals(live.Id, StringComparison.OrdinalIgnoreCase));
            if (a is not null)
            {
                a.TranscriptObservedUtc = DateTimeOffset.UtcNow;
                a.TranscriptPath = hit.Path;
                PersistUnlocked();
                live.TranscriptObservedUtc = a.TranscriptObservedUtc;
                live.TranscriptPath = a.TranscriptPath;
            }
        }

        return hit;
    }

    /// <summary>Scan Cursor agent-transcripts for canonical wake charge after <paramref name="afterUtc"/>.</summary>
    internal static (bool Observed, string? Path, int Scanned) ScanTranscriptsForNeedle(
        string needle,
        DateTimeOffset afterUtc,
        string? rootOverride,
        int maxFiles = 40)
    {
        var root = !string.IsNullOrWhiteSpace(rootOverride)
            ? rootOverride!
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cursor",
                "projects");
        if (!Directory.Exists(root))
            return (false, null, 0);

        var pad = afterUtc - TimeSpan.FromMinutes(2);
        var files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
            .Where(p => p.Contains("agent-transcripts", StringComparison.OrdinalIgnoreCase))
            .Select(p => new FileInfo(p))
            .Where(fi => fi.LastWriteTimeUtc >= pad.UtcDateTime)
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .Take(maxFiles)
            .ToList();

        var scanned = 0;
        foreach (var fi in files)
        {
            scanned++;
            try
            {
                foreach (var line in File.ReadLines(fi.FullName))
                {
                    if (line.Contains(needle, StringComparison.Ordinal))
                        return (true, fi.FullName, scanned);
                }
            }
            catch
            {
                /* skip locked/partial */
            }
        }

        return (false, null, scanned);
    }

    static void RecordSendEvidence(string id, bool ok, string? error)
    {
        lock (Gate)
        {
            var a = Arms.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (a is null) return;
            a.SendInvokedUtc ??= DateTimeOffset.UtcNow;
            a.SendOk = ok;
            a.SendError = ok ? null : error;
            PersistUnlocked();
        }
    }
}
