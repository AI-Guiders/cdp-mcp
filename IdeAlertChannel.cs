#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=alert</c> — EICAS-lite attention channel (ADR 0193).
/// Aggregates quality gates + disk drift + DAP stop into one sit pulse.
/// </summary>
internal static class IdeAlertChannel
{
    public const string SchemaVersion = "alert_channel/v1";

    public enum Level
    {
        Clear = 0,
        Warn = 1,
        Fail = 2
    }

    public sealed record Snap(
        Level Level,
        bool Ok,
        string Pulse,
        string[] Lines,
        int QualityFail,
        int QualityWarn,
        int DiskChanged,
        bool DapStopped,
        bool DapActive);

    public static Snap Build(
        QualityGates.QualitySnap quality,
        int diskChanged,
        bool dapActive,
        bool dapStopped)
    {
        var lines = new List<string>();
        var level = Level.Clear;

        if (quality is { Enabled: true, Fail: > 0 })
        {
            level = Level.Fail;
            lines.Add($"!gates FAIL×{quality.Fail} WARN×{quality.Warn}");
        }
        else if (quality is { Enabled: true, Warn: > 0 })
        {
            level = Level.Warn;
            lines.Add($"*gates WARN×{quality.Warn}");
        }

        if (diskChanged > 0)
        {
            if (level < Level.Warn) level = Level.Warn;
            lines.Add($"{(level == Level.Fail ? "|" : "*")}disk×{diskChanged} outside IDE");
        }

        if (dapStopped)
        {
            if (level < Level.Warn) level = Level.Warn;
            lines.Add($"{(level == Level.Fail ? "|" : "*")}dap STOPPED");
        }
        else if (dapActive)
        {
            lines.Add("·dap active");
        }

        if (lines.Count == 0)
            lines.Add("(clear — no gates/disk/dap alerts)");

        var pulse = level switch
        {
            Level.Fail => quality.Fail > 0
                ? $"alert FAIL · gates×{quality.Fail}"
                : "alert FAIL",
            Level.Warn => BuildWarnPulse(quality, diskChanged, dapStopped),
            _ => "alert · clear"
        };

        return new Snap(
            level,
            Ok: level != Level.Fail,
            pulse,
            lines.Take(16).ToArray(),
            quality.Fail,
            quality.Warn,
            diskChanged,
            dapStopped,
            dapActive);
    }

    static string BuildWarnPulse(QualityGates.QualitySnap quality, int disk, bool dapStopped)
    {
        if (quality is { Enabled: true, Warn: > 0 })
            return $"alert WARN · gates×{quality.Warn}";
        if (disk > 0)
            return $"alert WARN · disk×{disk}";
        if (dapStopped)
            return "alert WARN · dap stopped";
        return "alert WARN";
    }

    public static object Handle(
        QualityGates.QualitySnap quality,
        int diskChanged,
        bool dapActive,
        bool dapStopped,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = args;
        var snap = Build(quality, diskChanged, dapActive, dapStopped);
        return new
        {
            ok = snap.Ok,
            schema = SchemaVersion,
            role = "alert",
            go = "alert",
            detail = "pulse",
            level = snap.Level.ToString().ToLowerInvariant(),
            pulse = snap.Pulse,
            view = new { schema = SchemaVersion, lines = snap.Lines },
            quality = new { fail = snap.QualityFail, warn = snap.QualityWarn, pulse = quality.Pulse },
            disk_changed = snap.DiskChanged,
            dap = new { active = snap.DapActive, stopped = snap.DapStopped },
            hint = snap.Level == Level.Clear
                ? "EICAS clear. go=quality | disk_peek | debug when something beeps."
                : "Sit alert — drill: go=quality | disk_peek | debug. Not a sermon."
        };
    }

    public static object PulseCard(Snap snap) => new
    {
        schema = SchemaVersion,
        level = snap.Level.ToString().ToLowerInvariant(),
        ok = snap.Ok,
        pulse = snap.Pulse
    };
}
