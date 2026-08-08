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
        // show|share|face = open/search + latch Glass Face (operator eyes). Default peer dig stays lynx-only.
        var faceVerb = op is "show" or "share" or "face";
        op = NormalizeBrowserOp(op);
        if (faceVerb)
        {
            var hasUrl = !string.IsNullOrWhiteSpace(
                ExtractKeyedValue(raw, "url")
                ?? ExtractKeyedValue(raw, "href")
                ?? ExtractKeyedValue(raw, "uri"));
            var hasQ = !string.IsNullOrWhiteSpace(
                ExtractKeyedValue(raw, "q")
                ?? ExtractKeyedValue(raw, "query")
                ?? ExtractKeyedValue(raw, "text"));
            op = !hasUrl && hasQ ? "search" : "open";
        }

        var face = faceVerb || WantBrowserFaceFlag(raw);

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
                Detail: face ? "face" : "peer",
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
                Detail: face ? "face" : "peer",
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
                Detail: face ? "face" : "peer",
                Go: "browser",
                Reason: "browser_link_required");
        }

        return new Route(
            Verb.Browser,
            raw,
            Ok: true,
            Op: op,
            Detail: face ? "face" : "peer",
            Go: "browser");
    }

    /// <summary>face=true|show=true|share=true|to=operator — latch Glass WebAi Face. Default peer dig does not.</summary>
    internal static bool WantBrowserFaceFlag(string raw)
    {
        foreach (var key in new[] { "face", "show", "share" })
        {
            var v = ExtractKeyedValue(raw, key);
            if (IsTruthyFlag(v))
                return true;
        }

        var to = ExtractKeyedValue(raw, "to");
        if (to is { Length: > 0 }
            && (to.Equals("operator", StringComparison.OrdinalIgnoreCase)
                || to.Equals("human", StringComparison.OrdinalIgnoreCase)
                || to.Equals("face", StringComparison.OrdinalIgnoreCase)
                || to.Equals("sveta", StringComparison.OrdinalIgnoreCase)
                || to.Equals("света", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    static bool IsTruthyFlag(string? v) =>
        v is { Length: > 0 }
        && (v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("1", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase)
            || v.Equals("face", StringComparison.OrdinalIgnoreCase)
            || v.Equals("show", StringComparison.OrdinalIgnoreCase)
            || v.Equals("share", StringComparison.OrdinalIgnoreCase));

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
            // show|share|face remapped before Normalize when used as verb head
            "show" or "share" or "face" => "open",
            _ => op
        };

    static bool IsBrowserOp(string? op) =>
        op is "scene" or "which" or "open" or "search" or "dump" or "links"
            or "follow" or "back" or "forward" or "close";
}
