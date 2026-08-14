#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CdpMcp;

/// <summary>Atom/RSS peel for freshness digest entries.</summary>
internal static class IdeFreshnessFeed
{
    public sealed record Item(string Id, string Title, string? Published, string? Link, string? Summary);

    public static bool LooksLikeFeed(string? contentType, string body)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            (contentType.Contains("atom", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("rss", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)))
            return body.Contains("<feed", StringComparison.OrdinalIgnoreCase)
                || body.Contains("<rss", StringComparison.OrdinalIgnoreCase);

        var t = body.AsSpan().TrimStart();
        if (t.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("<feed", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("<rss", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static IReadOnlyList<Item> Parse(string body, int take = 5)
    {
        take = Math.Clamp(take, 1, 20);
        try
        {
            var doc = XDocument.Parse(body, LoadOptions.None);
            var root = doc.Root;
            if (root is null) return [];

            XNamespace atom = "http://www.w3.org/2005/Atom";
            if (root.Name.LocalName.Equals("feed", StringComparison.OrdinalIgnoreCase))
            {
                return root.Elements(atom + "entry").Concat(root.Elements("entry"))
                    .Take(take)
                    .Select(e => new Item(
                        (string?)e.Element(atom + "id") ?? (string?)e.Element("id") ?? "",
                        (string?)e.Element(atom + "title") ?? (string?)e.Element("title") ?? "",
                        (string?)e.Element(atom + "updated") ?? (string?)e.Element(atom + "published")
                            ?? (string?)e.Element("updated") ?? (string?)e.Element("published"),
                        (string?)e.Elements(atom + "link")
                            .FirstOrDefault(l =>
                            {
                                var rel = (string?)l.Attribute("rel");
                                return rel is null || rel == "alternate";
                            })
                            ?.Attribute("href")
                            ?? (string?)e.Element(atom + "link")?.Attribute("href")
                            ?? (string?)e.Element("link"),
                        Trunc((string?)e.Element(atom + "summary") ?? (string?)e.Element("summary") ?? (string?)e.Element(atom + "content"), 240)
                    ))
                    .Where(i => !string.IsNullOrWhiteSpace(i.Id) || !string.IsNullOrWhiteSpace(i.Title))
                    .ToList();
            }

            if (root.Name.LocalName.Equals("rss", StringComparison.OrdinalIgnoreCase))
            {
                var channel = root.Element("channel");
                if (channel is null) return [];
                return channel.Elements("item").Take(take).Select(e => new Item(
                    (string?)e.Element("guid") ?? (string?)e.Element("link") ?? "",
                    (string?)e.Element("title") ?? "",
                    (string?)e.Element("pubDate"),
                    (string?)e.Element("link"),
                    Trunc((string?)e.Element("description"), 240)
                )).ToList();
            }
        }
        catch
        {
            // not xml / malformed
        }

        return [];
    }

    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static string? Trunc(string? s, int n)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= n ? s : s[..n] + "…";
    }
}
