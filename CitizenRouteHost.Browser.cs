#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent browser — sync InternetBrowserHabitat.Dispatch; place browser organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<InternetBrowserHabitat?>? BrowserHabitatResolver { get; set; }

    /// <summary>Tests: inject fake browser JSON; live uses habitat Dispatch.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? BrowserDispatchOverride { get; set; }

    static Applied RunBrowser(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildBrowserArgs(route.Raw, op);

        try
        {
            string json;
            if (BrowserDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var habitat = BrowserHabitatResolver?.Invoke();
                if (habitat is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "browser",
                        Go: "browser",
                        Reason: "no_browser");
                }

                json = habitat.Dispatch(args);
            }

            var ok = TryReadBrowserOk(json);
            var pulse = TryReadBrowserPulse(json, op);
            var wantFace = string.Equals(route.Detail, "face", StringComparison.OrdinalIgnoreCase)
                || CitizenIntentRouter.WantBrowserFaceFlag(route.Raw)
                || WantBrowserFaceArgs(args);
            string? seat = null;
            if (wantFace && op is "open" or "search" or "follow" or "goto" or "navigate")
            {
                var faceUrl = TryReadBrowserFaceUrl(json, args);
                if (faceUrl is { Length: > 0 })
                    seat = IdeDeskSeats.PlaceOrgan("browser", faceUrl, showFace: true);
            }

            // peer = Sierra lynx dig (default). face = operator WebAiPortal latch.
            var modeBit = wantFace ? "face" : "peer";
            pulse = string.IsNullOrWhiteSpace(pulse)
                ? "browser " + modeBit
                : pulse + " · " + modeBit;
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "browser",
                Seat: seat,
                Go: "browser",
                Pulse: TruncPulse(pulse, InventoryObservePulseMax),
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "browser_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "browser",
                Go: "browser",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildBrowserArgs(string raw, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        foreach (var key in BrowserStringKeys)
        {
            var val = ExtractMcpKeyed(raw, key);
            if (val is { Length: > 0 })
                args[AliasBrowserKey(key)] = JsonSerializer.SerializeToElement(val);
        }

        // Face latch flags (not sent to lynx habitat — host PlaceOrgan gate).
        foreach (var key in new[] { "face", "show", "share", "to" })
        {
            var val = ExtractMcpKeyed(raw, key);
            if (val is { Length: > 0 })
                args[key] = JsonSerializer.SerializeToElement(val);
        }

        // Prefer canonical keys when aliases collide.
        if (ExtractMcpKeyed(raw, "url") is { Length: > 0 } url)
            args["url"] = JsonSerializer.SerializeToElement(url);
        else if (ExtractMcpKeyed(raw, "href") is { Length: > 0 } href)
            args["url"] = JsonSerializer.SerializeToElement(href);
        else if (ExtractMcpKeyed(raw, "uri") is { Length: > 0 } uri)
            args["url"] = JsonSerializer.SerializeToElement(uri);

        if (ExtractMcpKeyed(raw, "q") is { Length: > 0 } q)
            args["q"] = JsonSerializer.SerializeToElement(q);
        else if (ExtractMcpKeyed(raw, "query") is { Length: > 0 } query)
            args["q"] = JsonSerializer.SerializeToElement(query);
        else if (ExtractMcpKeyed(raw, "text") is { Length: > 0 } text && op == "search")
            args["q"] = JsonSerializer.SerializeToElement(text);

        if (ExtractMcpKeyed(raw, "link") is { Length: > 0 } linkRaw
            && int.TryParse(linkRaw, out var link))
            args["link"] = JsonSerializer.SerializeToElement(link);
        else if (ExtractMcpKeyed(raw, "n") is { Length: > 0 } nRaw
            && int.TryParse(nRaw, out var n))
            args["link"] = JsonSerializer.SerializeToElement(n);
        else if (ExtractMcpKeyed(raw, "ref") is { Length: > 0 } refRaw
            && int.TryParse(refRaw, out var refN))
            args["link"] = JsonSerializer.SerializeToElement(refN);

        PutBrowserInt(args, raw, "take");
        PutBrowserInt(args, raw, "width");
        PutBrowserInt(args, raw, "max_chars");
        PutBrowserInt(args, raw, "timeout_seconds");

        return args;
    }

    static readonly string[] BrowserStringKeys =
    [
        "url", "href", "uri", "q", "query", "text", "engine", "tab",
        "filter", "useragent", "link", "n", "ref"
    ];

    /// <summary>MCP/citizen args: face|show|share|to=operator → Glass Face. Default peer dig skips PlaceOrgan.</summary>
    internal static bool WantBrowserFaceArgs(IReadOnlyDictionary<string, JsonElement> args)
    {
        foreach (var key in new[] { "face", "show", "share" })
        {
            if (!args.TryGetValue(key, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.True)
                return true;
            if (el.ValueKind == JsonValueKind.String && IsTruthyBrowserFace(el.GetString()))
                return true;
        }

        if (args.TryGetValue("to", out var toEl)
            && toEl.ValueKind == JsonValueKind.String
            && toEl.GetString() is { Length: > 0 } to
            && (to.Equals("operator", StringComparison.OrdinalIgnoreCase)
                || to.Equals("human", StringComparison.OrdinalIgnoreCase)
                || to.Equals("face", StringComparison.OrdinalIgnoreCase)
                || to.Equals("sveta", StringComparison.OrdinalIgnoreCase)
                || to.Equals("света", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    static bool IsTruthyBrowserFace(string? v) =>
        v is { Length: > 0 }
        && (v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("1", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase)
            || v.Equals("face", StringComparison.OrdinalIgnoreCase)
            || v.Equals("show", StringComparison.OrdinalIgnoreCase)
            || v.Equals("share", StringComparison.OrdinalIgnoreCase));

    static string AliasBrowserKey(string key) =>
        key switch
        {
            "href" or "uri" => "url",
            "query" => "q",
            "n" or "ref" => "link",
            _ => key
        };

    static void PutBrowserInt(Dictionary<string, JsonElement> args, string raw, string key)
    {
        if (ExtractMcpKeyed(raw, key) is { Length: > 0 } rawVal
            && int.TryParse(rawVal, out var n))
            args[key] = JsonSerializer.SerializeToElement(n);
    }

    static bool TryReadBrowserOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            return root.TryGetProperty("op", out _) || root.TryGetProperty("pulse", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadBrowserPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());

            var bits = new List<string> { "browser", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("active_tab", out var tab) && tab.ValueKind == JsonValueKind.String
                && tab.GetString() is { Length: > 0 } tid)
                bits.Add(tid);
            if (root.TryGetProperty("tab_count", out var tc) && tc.TryGetInt32(out var nTabs))
                bits.Add("tabs=" + nTabs);
            if (root.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                && url.GetString() is { Length: > 0 } u)
                bits.Add(TruncPulse(u) ?? u);
            if (root.TryGetProperty("q", out var qEl) && qEl.ValueKind == JsonValueKind.String
                && qEl.GetString() is { Length: > 0 } q)
                bits.Add("q=" + (TruncPulse(q) ?? q));
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add("n=" + n);
            if (root.TryGetProperty("lynx", out var lynx) && lynx.ValueKind == JsonValueKind.String
                && lynx.GetString() is { Length: > 0 } path)
                bits.Add("lynx");
            if (root.TryGetProperty("lynx_error", out var le) && le.ValueKind == JsonValueKind.String
                && le.GetString() is { Length: > 0 } err)
                bits.Add(TruncPulse(err) ?? err);
            if (root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
                && textEl.GetString() is { Length: > 0 } body)
            {
                var one = body.Replace('\r', ' ').Replace('\n', ' ');
                while (one.Contains("  ", StringComparison.Ordinal))
                    one = one.Replace("  ", " ", StringComparison.Ordinal);
                one = one.Trim();
                if (one.Length > 0)
                    bits.Add("· " + one);
            }

            // Peer must see page body — URL-only is seeming internet (lived SoftFL).
            return TruncPulse(string.Join(' ', bits), InventoryObservePulseMax);
        }
        catch
        {
            return TruncPulse(json, InventoryObservePulseMax);
        }
    }

    /// <summary>URL for Glass WebAiPortal Face — lynx dump stays in pulse; human Face navigates WebView2.</summary>
    static string? TryReadBrowserFaceUrl(string json, IReadOnlyDictionary<string, JsonElement> args)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var key in new[] { "url", "search_url", "href", "uri" })
            {
                if (root.TryGetProperty(key, out var el)
                    && el.ValueKind == JsonValueKind.String
                    && el.GetString() is { Length: > 0 } u)
                    return u.Trim();
            }
        }
        catch
        {
            /* fall through to args */
        }

        foreach (var key in new[] { "url", "uri", "href" })
        {
            if (args.TryGetValue(key, out var el)
                && el.ValueKind == JsonValueKind.String
                && el.GetString() is { Length: > 0 } u)
                return u.Trim();
        }

        if (args.TryGetValue("q", out var qEl)
            && qEl.ValueKind == JsonValueKind.String
            && qEl.GetString() is { Length: > 0 } q)
            return "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(q);

        return null;
    }

}
