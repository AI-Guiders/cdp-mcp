#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// One-liner ops comfort: live build · staged/pending · continuity/await · seat.
/// Cheap — no CDT; safe on every desk/health pulse.
/// </summary>
internal static class IdeOpsPulse
{
    public static string Line()
    {
        var self = IdeDeploy.ResolveSelfInstallRoot();
        var seat = IdeDeploy.ClassifySeat(self);
        var liveUtc = TryLiveUtc();
        var pending = ReadPending(self) is not null;
        var cont = StripContPrefix(IdeIgniteArmHost.ContinuityPulseLine());
        var live = liveUtc is { } u ? u.ToString("HH:mm:ss") + "Z" : "?";
        var staged = pending ? "staged" : "clear";
        return $"ops · seat={seat} · live={live} · {staged} · {cont}";
    }

    public static object Snap()
    {
        var self = IdeDeploy.ResolveSelfInstallRoot();
        var seat = IdeDeploy.ClassifySeat(self);
        var liveUtc = TryLiveUtc();
        var pending = ReadPending(self);
        var contPulse = IdeIgniteArmHost.ContinuityPulseLine();
        return new
        {
            seat,
            store = IdeIgniteArmHost.StorePath,
            live_utc = liveUtc?.ToString("o"),
            live_short = liveUtc is { } u ? u.ToString("HH:mm:ss") + "Z" : "?",
            pending,
            continuity = IdeIgniteArmHost.ContinuitySlice(),
            continuity_pulse = contPulse,
            pulse = Line()
        };
    }

    static DateTimeOffset? TryLiveUtc()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return File.GetLastWriteTimeUtc(exe);
        }
        catch { /* ignore */ }

        return null;
    }

    static string StripContPrefix(string contPulse)
    {
        const string prefix = "ignite · continuity · ";
        return contPulse.StartsWith(prefix, StringComparison.Ordinal)
            ? contPulse[prefix.Length..]
            : contPulse;
    }

    static object? ReadPending(string? installRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installRoot)) return null;
            var path = Path.Combine(installRoot, "cdp-pending-update.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        }
        catch
        {
            return new { ok = false, error = "pending_unreadable" };
        }
    }
}
