#nullable enable
using System.Diagnostics;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// One-liner ops comfort: seat · self/sib version · lag · staged · continuity.
/// Cheap — no CDT; safe on every desk/health pulse (ADX: no shell archaeology).
/// </summary>
internal static class IdeOpsPulse
{
    public static string Line()
    {
        var card = SeatsCard();
        var liveUtc = TryLiveUtc();
        var pending = ReadPending(card.SelfRoot) is not null;
        var cont = StripContPrefix(IdeIgniteArmHost.ContinuityPulseLine());
        var live = liveUtc is { } u ? u.ToString("HH:mm:ss") + "Z" : "?";
        var staged = pending ? "staged" : "clear";
        var selfV = card.SelfVersion ?? "?";
        var sibV = card.SiblingVersion ?? "?";
        var lag = card.Lag ? " · lag" : "";
        return $"ops · seat={card.Seat} · self={selfV} · sib={sibV}{lag} · live={live} · {staged} · {cont}";
    }

    public static object Snap()
    {
        var card = SeatsCard();
        var liveUtc = TryLiveUtc();
        var pending = ReadPending(card.SelfRoot);
        var contPulse = IdeIgniteArmHost.ContinuityPulseLine();
        return new
        {
            seat = card.Seat,
            self_root = card.SelfRoot,
            sibling_root = card.SiblingRoot,
            self_version = card.SelfVersion,
            sibling_version = card.SiblingVersion,
            lag = card.Lag,
            store = IdeIgniteArmHost.StorePath,
            live_utc = liveUtc?.ToString("o"),
            live_short = liveUtc is { } u ? u.ToString("HH:mm:ss") + "Z" : "?",
            pending,
            continuity = IdeIgniteArmHost.ContinuitySlice(),
            continuity_pulse = contPulse,
            pulse = Line(),
            next = card.Lag
                ? new
                {
                    remount = "sibling ahead — go=deploy soft self / Reload MCP seat, or work on cdp-debug",
                    health = "cdp_health ops.self_version vs sibling_version"
                }
                : (object?)null
        };
    }

    /// <summary>Mirror ops pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        var card = SeatsCard();
        var pending = ReadPending(card.SelfRoot) is not null;
        var cont = StripContPrefix(IdeIgniteArmHost.ContinuityPulseLine());
        // Dark Cockpit: silent when clear + continuity idle (armed=0 only) + no lag.
        var continuityIdle = string.Equals(cont, "armed=0", StringComparison.Ordinal);
        var active = pending || !continuityIdle || card.Lag;
        CideSysLatch.Publish(active, Line(), card.Seat, pending);
    }

    public static SeatsSnap SeatsCard()
    {
        var selfRoot = IdeDeploy.ResolveSelfInstallRoot();
        var seat = IdeDeploy.ClassifySeat(selfRoot);
        var siblingRoot = SiblingRootForSeat(seat);
        var selfV = TryInstallProductVersion(selfRoot);
        var sibV = TryInstallProductVersion(siblingRoot);
        var lag = selfV is { Length: > 0 }
                  && sibV is { Length: > 0 }
                  && !string.Equals(selfV, sibV, StringComparison.OrdinalIgnoreCase);
        return new SeatsSnap(seat, selfRoot, siblingRoot, selfV, sibV, lag);
    }

    public static object SeatsWire()
    {
        var c = SeatsCard();
        return new
        {
            seat = c.Seat,
            self_root = c.SelfRoot,
            sibling_root = c.SiblingRoot,
            self_version = c.SelfVersion,
            sibling_version = c.SiblingVersion,
            lag = c.Lag
        };
    }

    internal static string SiblingRootForSeat(string seat) => seat switch
    {
        "cdp" => IdeDeploy.DebugTarget,
        "cdp-debug" => IdeDeploy.ReleaseTarget,
        _ => IdeDeploy.ReleaseTarget
    };

    /// <summary>ProductVersion without +commit / metadata suffix.</summary>
    internal static string? ShortVersion(string? productOrFileVersion)
    {
        if (string.IsNullOrWhiteSpace(productOrFileVersion))
            return null;
        var v = productOrFileVersion.Trim();
        var plus = v.IndexOf('+');
        if (plus > 0)
            v = v[..plus];
        var space = v.IndexOf(' ');
        if (space > 0)
            v = v[..space];
        return v.Length == 0 ? null : v;
    }

    internal static string? TryInstallProductVersion(string? installRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                return null;
            var exe = Path.Combine(installRoot, "CdpMcp.exe");
            if (!File.Exists(exe))
                return null;
            var info = FileVersionInfo.GetVersionInfo(exe);
            return ShortVersion(info.ProductVersion) ?? ShortVersion(info.FileVersion);
        }
        catch
        {
            return null;
        }
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

    public readonly record struct SeatsSnap(
        string Seat,
        string? SelfRoot,
        string SiblingRoot,
        string? SelfVersion,
        string? SiblingVersion,
        bool Lag);
}
