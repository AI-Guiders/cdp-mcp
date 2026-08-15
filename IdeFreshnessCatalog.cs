#nullable enable

namespace CdpMcp;

/// <summary>SSOT built-in watch aliases → URL (+ optional status domain for NRT).</summary>
internal static class IdeFreshnessCatalog
{
    public sealed record Entry(string Url, string? Domain = null, string? StatusFile = null);

    static readonly Dictionary<string, Entry> ByAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["baseline2026"] = new("https://web.dev/baseline/2026", "software-javascript", "status-javascript-v1.md"),
        ["baseline"] = new("https://web.dev/baseline/2026", "software-javascript", "status-javascript-v1.md"),
        ["php"] = new("https://www.php.net/releases/", "software-php-laravel", "status-php-laravel-v1.md"),
        ["php-releases"] = new("https://www.php.net/releases/", "software-php-laravel", "status-php-laravel-v1.md"),
        ["laravel"] = new("https://laravel.com/docs/13.x/releases", "software-php-laravel", "status-php-laravel-v1.md"),
        ["laravel-releases"] = new("https://laravel.com/docs/13.x/releases", "software-php-laravel", "status-php-laravel-v1.md"),
        ["avalonia"] = new("https://github.com/AvaloniaUI/Avalonia/releases.atom", "software-dotnet-avalonia", "status-avalonia-cascade-ide-ui-v1.md"),
        ["avalonia-releases"] = new("https://github.com/AvaloniaUI/Avalonia/releases.atom", "software-dotnet-avalonia", "status-avalonia-cascade-ide-ui-v1.md"),
        ["node"] = new("https://nodejs.org/en/blog/rss.xml", "software-javascript", "status-javascript-v1.md"),
        ["nodejs-releases"] = new("https://nodejs.org/en/blog/rss.xml", "software-javascript", "status-javascript-v1.md"),
    };

    static readonly Dictionary<string, Entry> ByUrl =
        ByAlias.Values
            .GroupBy(e => e.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, Entry> Aliases => ByAlias;

    public static bool TryResolve(string aliasOrUrl, out Entry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(aliasOrUrl)) return false;
        var key = aliasOrUrl.Trim();
        if (ByAlias.TryGetValue(key, out entry!)) return true;
        if (ByUrl.TryGetValue(key, out entry!)) return true;
        return false;
    }

    public static bool TryMapUrl(string aliasOrUrl, out string url)
    {
        url = "";
        if (!TryResolve(aliasOrUrl, out var e)) return false;
        url = e.Url;
        return true;
    }
}
