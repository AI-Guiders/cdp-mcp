#nullable enable
using System.Text;

namespace CdpMcp;

/// <summary>
/// Hands receipt domain (OOA&amp;D Kit) — SoftOrgan Face chips, not Intercom letter laundry.
/// Writer paints <c>hands-LATEST.json</c> chrome_hint; Glass catalog id <c>hands</c> / HND.
/// </summary>
internal static class CitizenHandsReceipt
{
    public enum Phase
    {
        Idle,
        Running,
        Done
    }

    public sealed record Item(string Label, bool Ok, string Tip);

    public sealed record Snapshot(
        Phase Phase,
        IReadOnlyList<Item> Items,
        TimeSpan? Elapsed,
        int OkCount,
        int FailCount);

    public static Snapshot Running(TimeSpan? elapsed = null) =>
        new(Phase.Running, [], elapsed, 0, 0);

    public static Snapshot Idle() =>
        new(Phase.Idle, [], null, 0, 0);

    public static Snapshot FromApplied(
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        TimeSpan? elapsed = null)
    {
        if (executed is null || executed.Count == 0)
            return new Snapshot(Phase.Done, [], elapsed, 0, 0);

        var items = new List<Item>(Math.Min(executed.Count, 6));
        var okN = 0;
        var failN = 0;
        foreach (var a in executed)
        {
            if (a.Ok)
                okN++;
            else
                failN++;
            var tip = FormatItemTip(a);
            if (string.IsNullOrWhiteSpace(tip))
                continue;
            items.Add(new Item(ChipLabel(a), a.Ok, tip));
            if (items.Count >= 6)
                break;
        }

        return new Snapshot(Phase.Done, items, elapsed, okN, failN);
    }

    /// <summary>SoftOrgan chrome_hint — ChipLevelFromHint keywords: RUNNING / FAIL / OK.</summary>
    public static string? FormatChromeHint(Snapshot snap)
    {
        if (snap.Phase == Phase.Idle)
            return null;

        var dur = FormatElapsed(snap.Elapsed);
        if (snap.Phase == Phase.Running)
        {
            return string.IsNullOrWhiteSpace(dur)
                ? "CAUTION · RUNNING"
                : "CAUTION · RUNNING · " + dur;
        }

        if (snap.Items.Count == 0)
            return null;

        var head = snap.FailCount > 0
            ? $"FAIL · ok×{snap.OkCount} · fail×{snap.FailCount}"
            : $"OK · ok×{snap.OkCount}";
        if (!string.IsNullOrWhiteSpace(dur))
            head += " · " + dur;

        var sb = new StringBuilder(head.Length + snap.Items.Count * 48);
        sb.Append(head);
        foreach (var item in snap.Items)
        {
            sb.Append('\n');
            sb.Append("• ");
            sb.Append(item.Tip);
        }

        return sb.ToString();
    }

    /// <summary>Legacy tip body (tests / SoftOrgan tooltip) — same shape as former FormatHands.</summary>
    public static string FormatTip(
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        TimeSpan? elapsed = null) =>
        FormatChromeHint(FromApplied(executed, elapsed)) ?? "";

    static string ChipLabel(CitizenRouteHost.Applied a)
    {
        if (!string.IsNullOrWhiteSpace(a.Go))
            return Trunc(a.Go!.Trim(), 8).ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(a.Verb))
            return Trunc(a.Verb.Trim(), 8).ToUpperInvariant();
        return "HND";
    }

    static string FormatItemTip(CitizenRouteHost.Applied a)
    {
        var core = !string.IsNullOrWhiteSpace(a.Go)
            ? a.Go!.Trim()
            : VerbRu(a.Verb);

        if (!string.IsNullOrWhiteSpace(a.Path))
        {
            var name = Path.GetFileName(a.Path.Trim());
            if (!string.IsNullOrWhiteSpace(name))
                core = string.IsNullOrWhiteSpace(core) ? name : core + " " + name;
        }

        if (string.IsNullOrWhiteSpace(core))
            core = "ход";

        if (!a.Ok)
        {
            var why = OneLine(a.Reason, 96);
            return string.IsNullOrWhiteSpace(why)
                ? core + " · fail · не вышло"
                : core + " · fail · " + why;
        }

        if (!string.IsNullOrWhiteSpace(a.Ship))
        {
            var ship = OneLine(a.Ship, 96);
            if (ship.Length > 0)
                return core + " · ok · ship " + ship;
        }

        if (!string.IsNullOrWhiteSpace(a.Pulse))
        {
            var tip = OneLine(a.Pulse, 120);
            if (tip.Length > 0)
                return core + " · ok · " + tip;
        }

        return core + " · ok";
    }

    static string VerbRu(string? verb)
    {
        if (string.IsNullOrWhiteSpace(verb))
            return "";
        return verb.Trim().ToLowerInvariant() switch
        {
            "go" or "drill" or "detail" => "открыла",
            "open" => "файл",
            "build" => "сборка",
            "test" => "тесты",
            "run" => "запуск",
            "git" => "git",
            "shell" => "shell",
            "pressure" => "pressure",
            "ignite" => "autoi",
            "cockpit" => "cockpit",
            "intercom" => "intercom",
            "browser" => "браузер",
            "kb" => "KB",
            "find" => "поиск",
            "replace" or "create" or "append" or "delete" => "правка",
            _ => verb.Trim().ToLowerInvariant()
        };
    }

    static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is null || elapsed.Value <= TimeSpan.Zero)
            return "";
        var s = (int)Math.Round(elapsed.Value.TotalSeconds);
        if (s < 1)
            s = 1;
        if (s < 60)
            return s + "s";
        var m = s / 60;
        var rem = s % 60;
        return rem == 0 ? m + "m" : m + "m" + rem + "s";
    }

    static string OneLine(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var t = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return Trunc(t, max);
    }

    static string Trunc(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return max <= 1 ? s[..1] : s[..(max - 1)] + "…";
    }
}
