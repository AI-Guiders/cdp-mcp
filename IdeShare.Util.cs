#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeShare
{
    public static string ResolveShareInbox(string? projectRoot, string? dirOverride)
    {
        if (!string.IsNullOrWhiteSpace(dirOverride))
            return Path.GetFullPath(dirOverride);
        if (!string.IsNullOrWhiteSpace(projectRoot))
            return Path.GetFullPath(Path.Combine(projectRoot, ".cdp", "share"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "share");
    }

    static string NormalizeWith(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        if (s is "operator" or "human" or "user" or "me" or "host")
            return "operator";
        return s;
    }

    static string NormalizeAsk(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "none";
        var s = raw.Trim().ToLowerInvariant();
        return s is "confirm" or "approve" or "yes" or "ask" ? "confirm"
            : s is "none" or "no" or "off" ? "none"
            : s;
    }

    static string Slug(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch is ' ' or '-' or '_' && sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "share" : s.Length <= 32 ? s : s[..32];
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') n++;
        }

        return n;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
