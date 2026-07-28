#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    /// <summary>Parse arm args into a new IgniteArm (does not persist).</summary>
    static bool TryCreateArm(IReadOnlyDictionary<string, JsonElement> args, out IgniteArm arm, out object? err)
    {
        arm = null!;
        err = null;
        var message = Opt(args, "message") ?? Opt(args, "text") ?? Opt(args, "msg") ?? Opt(args, "prompt");
        var task = Opt(args, "task") ?? Opt(args, "next") ?? Opt(args, "label");
        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(task))
        {
            err = Err("arm", "message_or_task_required", "arm task=… (TM label) and/or when=…; composer charge is canonical wake text");
            return false;
        }

        var when = NormalizeEvent(Opt(args, "when") ?? Opt(args, "event") ?? Opt(args, "on") ?? "timer");
        var inRaw = Opt(args, "in") ?? Opt(args, "after") ?? Opt(args, "delay");
        var port = OptInt(args, "port") ?? IdeIgniteChannel.DefaultPort;
        var chat = Opt(args, "chat") ?? Opt(args, "title");
        var once = OptBool(args, "once") ?? true;
        var lastOnce = ResolveLastOnce(args);
        if (lastOnce) once = true; // last_once implies once
        var okOnly = OptBool(args, "ok_only") ?? true;
        var settle = OptInt(args, "settle_seconds") ?? (when is "timer" or "manual" ? 2 : 8);
        var wait = OptInt(args, "wait_seconds") ?? 90;
        var id = Opt(args, "id");
        if (string.IsNullOrWhiteSpace(id))
            id = "arm-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                 Guid.NewGuid().ToString("N")[..6];

        DateTimeOffset? due = null;
        if (when == "timer")
        {
            if (!TryParseDue(inRaw, Opt(args, "at"), out due, out var perr))
            {
                err = Err("arm", "bad_timer", perr);
                return false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(inRaw) && TryParseDuration(inRaw!, out var d))
        {
            due = DateTimeOffset.UtcNow + d;
        }

        if (string.IsNullOrWhiteSpace(message))
            message = IdeIgniteChannel.CanonicalComposerCharge;

        var chargeMode = (Opt(args, "charge") ?? "minimal").Trim().ToLowerInvariant();

        arm = new IgniteArm
        {
            Id = id!,
            Event = when,
            Message = chargeMode is "custom" or "expand" or "legacy" ? message! : IdeIgniteChannel.CanonicalComposerCharge,
            ChargeMode = chargeMode,
            Task = task,
            Chat = chat,
            Port = port,
            Once = once,
            LastOnce = lastOnce,
            OkOnly = okOnly,
            SettleSeconds = Math.Clamp(settle, 0, 120),
            WaitSeconds = Math.Clamp(wait, 5, 600),
            DueUtc = due,
            InRaw = inRaw,
            Status = "armed",
            CreatedUtc = DateTimeOffset.UtcNow
        };
        return true;
    }

    static bool ResolveLastOnce(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (OptBool(args, "last_once") == true) return true;
        if (OptBool(args, "await_operator") == true) return true;
        var mode = (Opt(args, "mode") ?? "").Trim().ToLowerInvariant().Replace('-', '_');
        if (mode is "await" or "await_operator" or "last_once" or "idle") return true;
        return false;
    }

    public static string NormalizeEvent(string? raw)
    {
        var e = (raw ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return e switch
        {
            "build" or "build_done" or "build_ok" or "build_finished" or "on_build" => "build_finished",
            "test" or "tests" or "test_done" or "test_finished" or "on_test" => "test_finished",
            "shell" or "shell_done" or "shell_finished" or "on_shell" => "shell_finished",
            "time" or "delay" or "sleep" or "timer" or "in" => "timer",
            "manual" or "now" or "fire" => "manual",
            _ when e.Length == 0 => "timer",
            _ => e
        };
    }

    public static bool TryParseDuration(string raw, out TimeSpan span)
    {
        span = default;
        var s = raw.Trim().ToLowerInvariant();
        var m = Regex.Match(s, @"^(\d+)\s*(ms|s|m|h|d|sec|secs|second|seconds|min|mins|minute|minutes|hr|hrs|hour|hours|day|days)?$");
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out var n) || n < 0) return false;
        var u = m.Groups[2].Value;
        span = u switch
        {
            "ms" => TimeSpan.FromMilliseconds(n),
            "" or "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.FromSeconds(n),
            "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(n),
            "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(n),
            "d" or "day" or "days" => TimeSpan.FromDays(n),
            _ => TimeSpan.Zero
        };
        return span > TimeSpan.Zero || n == 0;
    }

    static bool TryParseDue(string? inRaw, string? atRaw, out DateTimeOffset? due, out string error)
    {
        due = null;
        error = "";
        if (!string.IsNullOrWhiteSpace(atRaw)
            && DateTimeOffset.TryParse(atRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            due = at;
            return true;
        }

        if (string.IsNullOrWhiteSpace(inRaw))
        {
            error = "timer requires in=30s|5m|2h or at=ISO-8601";
            return false;
        }

        if (!TryParseDuration(inRaw!, out var d))
        {
            error = $"bad duration '{inRaw}' (use 30s|5m|2h)";
            return false;
        }

        due = DateTimeOffset.UtcNow + d;
        return true;
    }

}
