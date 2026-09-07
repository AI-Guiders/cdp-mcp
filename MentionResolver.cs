#nullable enable

using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>Resolved mention: roster-nick + excerpt around its own occurrence.</summary>
public sealed record Mention(string Nick, string Excerpt);

/// <summary>
/// Owning layer of mention semantics (Света 2026-09-08, DDD): parse → roster-resolve →
/// self-skip → per-mention excerpt. One entry for every wake surface (forum post,
/// intercom letter); surfaces must not re-implement fragments.
/// Rules (each from a named lesson):
/// - backtick spans are not addresses (Тень 2026-09-07: три self-wake из постов ОБ упоминаниях);
/// - unknown nicks drop silently — a mention is a courtesy bell, not a hard address;
/// - the author never wakes himself (эхолалия);
/// - a letter carries its own context — excerpt around the recipient's OWN mention
///   (Света 2026-09-08: общий excerpt от начала показывал чужой абзац).
/// </summary>
public static class MentionResolver
{
    static readonly Regex CodeFenceRegex = new("```.*?```", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    static readonly Regex CodeSpanRegex = new("`[^`]*`", RegexOptions.CultureInvariant);
    static readonly Regex NickRegex = new(@"@[\wа-яё\-]+", RegexOptions.CultureInvariant);

    public static IReadOnlyList<Mention> Resolve(string? body, string? authorNick)
    {
        var found = new List<Mention>();
        if (string.IsNullOrWhiteSpace(body))
            return found;

        var prose = CodeFenceRegex.Replace(body, " ");
        prose = CodeSpanRegex.Replace(prose, " ");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in NickRegex.Matches(prose))
        {
            var nick = m.ToString().TrimStart('@');
            if (nick.Length == 0 || !seen.Add(nick))
                continue;
            if (CideIntercomAgents.Resolve(nick) is null)
                continue; // courtesy bell — незарегистрированных не будим
            if (authorNick is not null &&
                nick.Equals(authorNick.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase))
                continue; // самостук не будит (урок эхолалии)

            found.Add(new Mention(nick, ExcerptAround(body.Trim(), nick)));
        }

        return found;
    }

    internal static string ExcerptAround(string text, string mentioned)
    {
        var at = text.IndexOf($"@{mentioned}", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return text.Length > 160 ? text[..160] + "…" : text;

        var from = Math.Max(0, at - 40);
        var len = Math.Min(text.Length - from, 200);
        return (from > 0 ? "…" : "") + text.Substring(from, len) +
               (from + len < text.Length ? "…" : "");
    }
}
