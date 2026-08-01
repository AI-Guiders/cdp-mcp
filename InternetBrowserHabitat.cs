using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// Agent internet browser habitat — lynx dump engine inside CDP (ADR 0188).
/// Control is the agent's (scene_internet_browser), not the host Cursor Browser.
/// </summary>
internal sealed partial class InternetBrowserHabitat
{
    public const string Schema = "internet_browser_scene/v1";
    public const int MaxTabs = 8;
    public const int DefaultWidth = 100;
    public const int DefaultTimeoutSeconds = 45;
    public const int ScenePreviewChars = 160;
    public const int DumpBodyChars = 24_000;
    public const int MaxHistory = 40;

    /// <summary>Spoof modern Chromium so corporate gates stop dumping «update your browser» at Lynx/2.x.</summary>
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    /// <summary>Sovereign search — HTML DDG, not Google JS farm.</summary>
    public const string DefaultSearchEngine = "ddg";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly Regex RefLine = new(
        @"^\s*(\d+)\.\s+(\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    readonly ConcurrentDictionary<string, Tab> _tabs = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();
    string? _activeTab = "main";

    public string Dispatch(IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = Opt(args, "op") ?? "scene";
        return op.Trim().ToLowerInvariant() switch
        {
            "scene" or "status" or "list" => SceneJson(),
            "which" or "engine" => WhichJson(),
            "open" or "goto" or "navigate" => Open(args),
            "search" or "find" or "google" => Search(args),
            "dump" or "read" or "page" or "last" => Dump(args),
            "links" or "refs" => Links(args),
            "follow" or "click" => Follow(args),
            "back" => Back(args),
            "forward" or "fwd" => Forward(args),
            "close" or "tab_close" => Close(args),
            _ => Fail("unknown_op", null, "op=scene|which|open|search|dump|links|follow|back|forward|close")
        };
    }

    public string SceneJson()
    {
        EnsureMain();
        var engine = ResolveLynx();
        var tabs = _tabs.Values
            .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Card(ScenePreviewChars, _activeTab))
            .ToList();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "scene",
            engine = "lynx",
            lynx = engine.Ok ? engine.Path : null,
            lynx_error = engine.Ok ? null : engine.Error,
            user_agent = ResolveUserAgent(),
            search_default = IdeSettingsHabitat.EffectiveSearchEngine(),
            active_tab = _activeTab,
            tab_count = tabs.Count,
            tabs,
            next =
                new object[]
                {
                    new { go = "internet_browser_search", label = "Search", why = "q=… (DDG HTML default)" },
                    new { go = "internet_browser_open", label = "Open", why = "url=https://…" },
                    new { go = "internet_browser_dump", label = "Dump", why = "read page text" },
                    new { go = "internet_browser_links", label = "Links", why = "numbered refs → follow" },
                    new { go = "internet_browser_follow", label = "Follow", why = "link=N" }
                },
            hint =
                "Agent IDE internet browser (lynx + Chromium UA spoof). NOT Cursor Browser. " +
                "Search: op=search q= → DuckDuckGo HTML (Google SERP still JS-gated)."
        }, Pretty);
    }

    /// <summary>Cheap desk pulse for cockpit tiles / loci (no re-fetch).</summary>
    public BrowserPulse Pulse()
    {
        EnsureMain();
        var engine = ResolveLynx();
        Tab? active = null;
        if (_activeTab is { } aid)
            _tabs.TryGetValue(aid, out active);
        active ??= _tabs.Values.FirstOrDefault();
        var url = active?.Current?.Url;
        var preview = active?.Current is null
            ? null
            : Cap(active.Current.Text.Replace('\n', ' ').Replace('\r', ' '), 80);
        var pulse = !engine.Ok
            ? "lynx missing"
            : url is null
                ? "idle (no page)"
                : $"{_tabs.Count} tab(s) · {TruncateHost(url)}";
        return new BrowserPulse(engine.Ok, pulse, _activeTab ?? "main", _tabs.Count, url, preview, engine.Path);
    }

    public readonly record struct BrowserPulse(
        bool Ok,
        string Line,
        string ActiveTab,
        int TabCount,
        string? Url,
        string? Preview,
        string? LynxPath);

    static string TruncateHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            return u.Host + (u.AbsolutePath is "/" or "" ? "" : u.AbsolutePath.Length > 24
                ? u.AbsolutePath[..24] + "…"
                : u.AbsolutePath);
        return url.Length <= 48 ? url : url[..48] + "…";
    }

    public string WhichJson()
    {
        var engine = ResolveLynx();
        string? version = null;
        if (engine.Ok)
        {
            try
            {
                var (code, stdout, _) = RunProcess(engine.Path!, ["-version"], 10);
                if (code == 0)
                    version = FirstLine(stdout);
            }
            catch
            {
                /* best effort */
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = engine.Ok,
            op = "which",
            engine = "lynx",
            path = engine.Path,
            version,
            user_agent = ResolveUserAgent(),
            search_default = IdeSettingsHabitat.EffectiveSearchEngine(),
            error = engine.Error,
            hint = engine.Ok ? null : "scoop install lynx  (or set CDP_LYNX= path to lynx.exe)"
        }, Pretty);
    }

}
