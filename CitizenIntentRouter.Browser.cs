#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent browser|internet_browser — lynx habitat without Cursor Browser (go=browser place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteBrowser(string raw)
    {
        var op = ExtractKeyedValue(raw, "op") ?? ExtractKeyedValue(raw, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (raw.StartsWith("browser ", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("internet_browser ", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("web ", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("lynx ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = raw.IndexOf(' ');
                var rest = sp < 0 ? "" : raw[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizeBrowserOp(op);

        if (!IsBrowserOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "browser_op_unknown");

        if (op is "open"
            && string.IsNullOrWhiteSpace(
                ExtractKeyedValue(raw, "url")
                ?? ExtractKeyedValue(raw, "href")
                ?? ExtractKeyedValue(raw, "uri")))
        {
            return new Route(
                Verb.Browser,
                raw,
                Ok: false,
                Op: op,
                Go: "browser",
                Reason: "browser_url_required");
        }

        if (op is "search"
            && string.IsNullOrWhiteSpace(
                ExtractKeyedValue(raw, "q")
                ?? ExtractKeyedValue(raw, "query")
                ?? ExtractKeyedValue(raw, "text")))
        {
            return new Route(
                Verb.Browser,
                raw,
                Ok: false,
                Op: op,
                Go: "browser",
                Reason: "browser_query_required");
        }

        if (op is "follow"
            && string.IsNullOrWhiteSpace(
                ExtractKeyedValue(raw, "link")
                ?? ExtractKeyedValue(raw, "n")
                ?? ExtractKeyedValue(raw, "ref")))
        {
            return new Route(
                Verb.Browser,
                raw,
                Ok: false,
                Op: op,
                Go: "browser",
                Reason: "browser_link_required");
        }

        return new Route(
            Verb.Browser,
            raw,
            Ok: true,
            Op: op,
            Go: "browser");
    }

    static string NormalizeBrowserOp(string op) =>
        op switch
        {
            "status" or "list" or "tabs" => "scene",
            "engine" => "which",
            "goto" or "navigate" or "nav" => "open",
            "find" or "google" or "ddg" => "search",
            "read" or "page" or "last" => "dump",
            "refs" => "links",
            "click" => "follow",
            "fwd" => "forward",
            "tab_close" => "close",
            _ => op
        };

    static bool IsBrowserOp(string? op) =>
        op is "scene" or "which" or "open" or "search" or "dump" or "links"
            or "follow" or "back" or "forward" or "close";
}
