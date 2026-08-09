#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// Human-faced Intercom surface: strip wire (@intent/@event/@frame) from prose;
/// harness outcomes → receipt block (ok/fail/reason/pulse/elapsed) in the letter — not StatusText chrome.
/// </summary>
internal static partial class CitizenIntercomHumanSurface
{
    static readonly Regex WireLine = WireLineRegex();

    /// <summary>Publish body for Glass Intercom (prose + optional hands).</summary>
    public static string Publish(
        string? prose,
        IReadOnlyList<CitizenRouteHost.Applied>? executed = null,
        TimeSpan? elapsed = null)
    {
        var clean = StripWire(prose);
        var hands = FormatHands(executed, elapsed);
        if (string.IsNullOrWhiteSpace(hands))
        {
            if (string.IsNullOrWhiteSpace(clean))
                return "";
            var alone = clean.TrimEnd();
            var dur = FormatElapsed(elapsed);
            return string.IsNullOrWhiteSpace(dur) ? alone : alone + "\n\n⏱ " + dur;
        }

        if (string.IsNullOrWhiteSpace(clean))
            return hands;
        return clean.TrimEnd() + "\n\n" + hands;
    }

    /// <summary>Drop @intent / @event / @frame lines and peer-wire tips; keep human prose.</summary>
    public static string StripWire(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        var sb = new StringBuilder();
        var blankRun = 0;
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var t = line.TrimStart();
            if (t.Length == 0)
            {
                if (sb.Length == 0 || blankRun > 0)
                    continue;
                blankRun++;
                sb.Append('\n');
                continue;
            }

            blankRun = 0;
            if (IsWireLine(t))
                continue;

            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(line);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Harness → human-faced receipt (RU). Operator needs ok/fail/reason/pulse — not opaque «Сделала KB».
    /// </summary>
    public static string FormatHands(
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        TimeSpan? elapsed = null)
    {
        if (executed is null || executed.Count == 0)
            return "";

        var parts = new List<string>(executed.Count);
        var okN = 0;
        var failN = 0;
        foreach (var a in executed)
        {
            if (a.Ok)
                okN++;
            else
                failN++;
            var label = HumanLabel(a);
            if (string.IsNullOrWhiteSpace(label))
                continue;
            parts.Add(label);
            if (parts.Count >= 6)
                break;
        }

        if (parts.Count == 0)
            return "";

        var head = $"Сделала · ok×{okN}";
        if (failN > 0)
            head += $" · fail×{failN}";
        var dur = FormatElapsed(elapsed);
        if (!string.IsNullOrWhiteSpace(dur))
            head += " · " + dur;

        // One receipt block in the letter (not StatusText chrome).
        var sb = new StringBuilder(head.Length + parts.Count * 48);
        sb.Append(head);
        foreach (var p in parts)
        {
            sb.Append('\n');
            sb.Append("• ");
            sb.Append(p);
        }

        return sb.ToString();
    }

    static string HumanLabel(CitizenRouteHost.Applied a)
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

    internal static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is not { } e || e < TimeSpan.FromMilliseconds(500))
            return "";
        if (e.TotalSeconds < 60)
            return e.TotalSeconds < 10
                ? $"{e.TotalSeconds:0.0}s"
                : $"{e.TotalSeconds:0}s";
        return $"{(int)e.TotalMinutes}m{e.Seconds:00}s";
    }

    static string VerbRu(string verb)
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

    /// <summary>
    /// @frame desk / Autoi charge mirrored as Citizen — instrument dump, not human Radio.
    /// </summary>
    public static bool LooksLikeSaInstrumentWall(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var t = body;
        if (t.Contains("truncated habitat wake", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("`tm |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("**`tm |", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("`board |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("board | P:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("`peer |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`dialog |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`presence |", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("operator_priority", StringComparison.OrdinalIgnoreCase)
            && t.Length > 240)
            return true;

        return false;
    }

    static bool IsWireLine(string t)
    {
        if (t.StartsWith("@intent", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@event", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@frame", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@pulse", StringComparison.OrdinalIgnoreCase))
            return true;

        // Peer tip: ok · gen=… · mcp=live · ack=
        if (t.StartsWith("ok · gen=", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains(" · mcp=live · ", StringComparison.Ordinal)
            && t.Contains("ack=", StringComparison.Ordinal))
            return true;

        // @frame desk SA instrument bullets (stay out of human Intercom prose).
        if (IsSaInstrumentLine(t))
            return true;

        // @event table rows: "kind  | intent_ack"
        if (WireLine.IsMatch(t))
            return true;

        return false;
    }

    static bool IsSaInstrumentLine(string t)
    {
        var s = t.TrimStart('-', '*', ' ', '`');
        if (s.StartsWith("tm |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("board |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("peer |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("dialog |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("sticky |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("presence |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("cost |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("sa |", StringComparison.OrdinalIgnoreCase))
            return true;

        // Markdown-wrapped: - **`tm | …`**
        if (t.Contains("`tm |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`board |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`peer |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`dialog |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`presence |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`sticky |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`cost |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`sa |", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    static string OneLine(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        var t = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        if (t.Length > max)
            t = t[..(max - 1)] + "…";
        return t;
    }

    [GeneratedRegex(@"^(kind|id|ack|pulse)\s*\|\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WireLineRegex();
}
