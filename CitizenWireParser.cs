#nullable enable

namespace CdpMcp;

/// <summary>
/// Line-oriented citizen wire parser (<c>@frame</c> / <c>@intent</c> / <c>@event</c>).
/// Pure; unused by Cursor guest until habitat host injects frames (citizen-agent-wire-v0).
/// </summary>
internal static class CitizenWireParser
{
    public enum Kind
    {
        Frame,
        Intent,
        Event,
        Unknown
    }

    public sealed record Message(
        Kind Kind,
        string Type,
        string? Version,
        string? IntentText,
        IReadOnlyDictionary<string, string> Fields,
        string? TrailingBody);

    public static IReadOnlyList<Message> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var list = new List<Message>();
        var i = 0;
        while (i < lines.Length)
        {
            var raw = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(raw))
            {
                i++;
                continue;
            }

            if (!TryParseHeader(raw, out var kind, out var type, out var version, out var intentText))
            {
                i++;
                continue;
            }

            i++;
            var (fields, trailing) = ReadBody(lines, ref i);
            list.Add(new Message(kind, type, version, intentText, fields, trailing));
        }

        return list;
    }

    static (Dictionary<string, string> Fields, string? Trailing) ReadBody(string[] lines, ref int i)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyLines = new List<string>();
        var inTrailing = false;

        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd();
            if (line.Length > 0 && line[0] == '@' && TryParseHeader(line, out _, out _, out _, out _))
                break;

            i++;
            if (line == "---")
            {
                inTrailing = true;
                continue;
            }

            if (inTrailing)
            {
                bodyLines.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var pipe = line.IndexOf('|');
            if (pipe <= 0)
            {
                bodyLines.Add(line);
                inTrailing = true;
                continue;
            }

            var key = line[..pipe].Trim();
            var val = line[(pipe + 1)..].Trim();
            if (key.Length > 0)
                fields[key] = val;
        }

        string? trailing = bodyLines.Count > 0
            ? string.Join('\n', bodyLines).TrimEnd()
            : null;
        return (fields, trailing);
    }

    static bool TryParseHeader(
        string line,
        out Kind kind,
        out string type,
        out string? version,
        out string? intentText)
    {
        kind = Kind.Unknown;
        type = "";
        version = null;
        intentText = null;

        if (line.StartsWith("@frame ", StringComparison.OrdinalIgnoreCase))
        {
            kind = Kind.Frame;
            SplitTypeVersion(line["@frame ".Length..].Trim(), out type, out version);
            return type.Length > 0;
        }

        if (line.StartsWith("@event ", StringComparison.OrdinalIgnoreCase))
        {
            kind = Kind.Event;
            SplitTypeVersion(line["@event ".Length..].Trim(), out type, out version);
            return type.Length > 0;
        }

        if (line.StartsWith("@intent", StringComparison.OrdinalIgnoreCase))
        {
            kind = Kind.Intent;
            type = "intent";
            intentText = line.Length > "@intent".Length
                ? line["@intent".Length..].Trim()
                : "";
            return true;
        }

        return false;
    }

    static void SplitTypeVersion(string rest, out string type, out string? version)
    {
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        type = parts.Length > 0 ? parts[0] : "";
        version = parts.Length > 1 ? parts[1] : null;
    }
}
