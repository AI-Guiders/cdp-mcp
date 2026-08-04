#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// Human-faced Intercom surface: strip wire (@intent/@event/@frame) from prose;
/// harness outcomes → short «сделала: …» — not peer tip dump.
/// </summary>
internal static partial class CitizenIntercomHumanSurface
{
    static readonly Regex WireLine = WireLineRegex();

    /// <summary>Publish body for Glass Intercom (prose + optional hands).</summary>
    public static string Publish(string? prose, IReadOnlyList<CitizenRouteHost.Applied>? executed = null)
    {
        var clean = StripWire(prose);
        var hands = FormatHands(executed);
        if (string.IsNullOrWhiteSpace(hands))
            return clean;
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

    /// <summary>Harness → one human line (RU). Empty when nothing applied.</summary>
    public static string FormatHands(IReadOnlyList<CitizenRouteHost.Applied>? executed)
    {
        if (executed is null || executed.Count == 0)
            return "";

        var parts = new List<string>(executed.Count);
        foreach (var a in executed)
        {
            var label = HumanLabel(a);
            if (string.IsNullOrWhiteSpace(label))
                continue;
            parts.Add(label);
            if (parts.Count >= 4)
                break;
        }

        if (parts.Count == 0)
            return "";
        return "Сделала: " + string.Join(" · ", parts);
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
            return core + " — не вышло";

        if (!string.IsNullOrWhiteSpace(a.Pulse))
        {
            var tip = OneLine(a.Pulse, 36);
            if (tip.Length > 0 && !tip.Contains(core, StringComparison.OrdinalIgnoreCase))
                return core + " · " + tip;
        }

        return core;
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

        // @event table rows: "kind  | intent_ack"
        if (WireLine.IsMatch(t))
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
