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
internal sealed class InternetBrowserHabitat
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

    string Search(IReadOnlyDictionary<string, JsonElement> args)
    {
        var q = Opt(args, "q") ?? Opt(args, "query") ?? Opt(args, "text");
        if (string.IsNullOrWhiteSpace(q))
            return Fail("q_required", null, "op=search q=your query");

        var engine = (Opt(args, "engine") ?? Opt(args, "via") ?? IdeSettingsHabitat.EffectiveSearchEngine())
            .Trim()
            .ToLowerInvariant();

        string url = engine switch
        {
            "ddg" or "duck" or "duckduckgo" =>
                "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(q!),
            "google" or "g" =>
                "https://www.google.com/search?q=" + Uri.EscapeDataString(q!),
            "bing" =>
                "https://www.bing.com/search?q=" + Uri.EscapeDataString(q!),
            _ => ""
        };

        if (url.Length == 0)
            return Fail("unknown_engine", null, "engine=ddg|google|bing (default ddg)");

        var openArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["url"] = JsonSerializer.SerializeToElement(url)
        };
        foreach (var key in new[] { "tab", "width", "timeout_seconds", "max_chars", "useragent", "ua" })
        {
            if (args.TryGetValue(key, out var el))
                openArgs[key] = el;
        }

        if (!openArgs.ContainsKey("tab"))
            openArgs["tab"] = JsonSerializer.SerializeToElement("search");

        var result = Open(openArgs);
        // Annotate search meta without re-parse: Open already returns JSON; prepend note via wrap only if fail/ok.
        return AnnotateSearch(result, q!, engine, url);
    }

    static string AnnotateSearch(string openJson, string q, string engine, string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(openJson);
            var root = doc.RootElement;
            var dict = new Dictionary<string, object?>();
            foreach (var p in root.EnumerateObject())
            {
                dict[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.TryGetInt64(out var l) ? l : p.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => JsonSerializer.Deserialize<object>(p.Value.GetRawText())
                };
            }

            dict["op"] = "search";
            dict["query"] = q;
            dict["engine"] = engine;
            dict["search_url"] = url;
            if (engine is "google" or "g"
                && root.TryGetProperty("text", out var textEl)
                && textEl.GetString() is { } t
                && (t.Contains("enablejs", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Obnovite brauzer", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Update your browser", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("JavaScript", StringComparison.OrdinalIgnoreCase)))
            {
                dict["hint"] =
                    "Google SERP is JS-gated even with Chromium UA spoof. Prefer engine=ddg (default).";
            }
            else if (engine is "ddg" or "duck" or "duckduckgo")
            {
                dict["hint"] = "DDG HTML — follow link=N for results. Google remains optional/engine=google.";
            }

            return JsonSerializer.Serialize(dict, Pretty);
        }
        catch
        {
            return openJson;
        }
    }

    string Open(IReadOnlyDictionary<string, JsonElement> args)
    {
        var url = Opt(args, "url") ?? Opt(args, "uri") ?? Opt(args, "href");
        if (string.IsNullOrWhiteSpace(url))
            return Fail("url_required", null, "url=https://…");

        url = NormalizeUrl(url!);
        var tabId = SanitizeTab(Opt(args, "tab") ?? _activeTab ?? "main");
        var width = OptInt(args, "width") ?? IdeSettingsHabitat.EffectiveWidth();
        width = Math.Clamp(width, 40, 200);
        var timeout = OptInt(args, "timeout_seconds") ?? IdeSettingsHabitat.EffectiveTimeout();
        timeout = Math.Clamp(timeout, 5, 120);
        var maxChars = OptInt(args, "max_chars") ?? IdeSettingsHabitat.EffectiveDumpChars();

        var engine = ResolveLynx();
        if (!engine.Ok)
            return Fail("lynx_missing", tabId, engine.Error);

        var userAgent = Opt(args, "useragent") ?? Opt(args, "ua") ?? ResolveUserAgent();

        PageFetch fetch;
        try
        {
            fetch = FetchPage(engine.Path!, url, width, timeout, userAgent);
        }
        catch (Exception ex)
        {
            return Fail("fetch_failed", tabId, ex.Message);
        }

        lock (_gate)
        {
            if (_tabs.Count >= MaxTabs && !_tabs.ContainsKey(tabId))
                return Fail("too_many_tabs", tabId, $"Max {MaxTabs} — close= a tab first");

            var tab = GetOrCreateUnlocked(tabId);
            tab.Push(fetch);
            _activeTab = tab.Id;
        }

        return PageResult("open", tabId, fetch, maxChars, includeLinksSample: true);
    }

    string Dump(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!TryTab(args, out var tab, out var err))
            return err!;
        if (tab.Current is null)
            return Fail("empty_tab", tab.Id, "op=open url= first");

        var maxChars = OptInt(args, "max_chars") ?? IdeSettingsHabitat.EffectiveDumpChars();
        return PageResult("dump", tab.Id, tab.Current, maxChars, includeLinksSample: false);
    }

    string Links(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!TryTab(args, out var tab, out var err))
            return err!;
        if (tab.Current is null)
            return Fail("empty_tab", tab.Id, "op=open url= first");

        var take = OptInt(args, "take") ?? 80;
        take = Math.Clamp(take, 1, 500);
        var filter = Opt(args, "filter") ?? Opt(args, "q");
        IEnumerable<LinkRef> q = tab.Current.Links;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            q = q.Where(l =>
                l.Url.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (l.Label?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = q.Take(take).ToList();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "links",
            tab = tab.Id,
            url = tab.Current.Url,
            total = tab.Current.Links.Count,
            shown = list.Count,
            filter,
            links = list.Select(l => new { n = l.N, url = l.Url, label = l.Label }),
            hint = "op=follow link=N  (or open url=)"
        }, Pretty);
    }

    string Follow(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!TryTab(args, out var tab, out var err))
            return err!;
        if (tab.Current is null)
            return Fail("empty_tab", tab.Id, "op=open url= first");

        var n = OptInt(args, "link") ?? OptInt(args, "n") ?? OptInt(args, "ref");
        if (n is null or < 1)
            return Fail("link_required", tab.Id, "link=N from op=links");

        var hit = tab.Current.Links.FirstOrDefault(l => l.N == n.Value);
        if (hit is null)
            return Fail("link_not_found", tab.Id, $"No ref {n}; op=links");

        var openArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["url"] = JsonSerializer.SerializeToElement(hit.Url),
            ["tab"] = JsonSerializer.SerializeToElement(tab.Id)
        };
        if (args.TryGetValue("width", out var w)) openArgs["width"] = w;
        if (args.TryGetValue("timeout_seconds", out var t)) openArgs["timeout_seconds"] = t;
        if (args.TryGetValue("max_chars", out var m)) openArgs["max_chars"] = m;
        return Open(openArgs);
    }

    string Back(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!TryTab(args, out var tab, out var err))
            return err!;
        if (!tab.TryBack(out var page))
            return Fail("no_back", tab.Id, null);
        var maxChars = OptInt(args, "max_chars") ?? DumpBodyChars;
        return PageResult("back", tab.Id, page!, maxChars, includeLinksSample: true);
    }

    string Forward(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!TryTab(args, out var tab, out var err))
            return err!;
        if (!tab.TryForward(out var page))
            return Fail("no_forward", tab.Id, null);
        var maxChars = OptInt(args, "max_chars") ?? DumpBodyChars;
        return PageResult("forward", tab.Id, page!, maxChars, includeLinksSample: true);
    }

    string Close(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = SanitizeTab(Opt(args, "tab") ?? _activeTab ?? "main");
        if (!_tabs.TryRemove(id, out _))
            return Fail("tab_missing", id, null);

        if (string.Equals(_activeTab, id, StringComparison.OrdinalIgnoreCase))
            _activeTab = _tabs.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? "main";

        EnsureMain();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "close",
            tab = id,
            active_tab = _activeTab,
            remaining = _tabs.Count
        }, Pretty);
    }

    string PageResult(string op, string tabId, PageFetch page, int maxChars, bool includeLinksSample)
    {
        maxChars = Math.Clamp(maxChars, 256, 200_000);
        var body = Cap(page.Text, maxChars);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = page.ExitCode == 0,
            op,
            tab = tabId,
            url = page.Url,
            exit_code = page.ExitCode,
            elapsed_ms = page.ElapsedMs,
            chars = page.Text.Length,
            truncated = page.Text.Length > body.Length,
            link_count = page.Links.Count,
            links_sample = includeLinksSample
                ? page.Links.Take(12).Select(l => new { n = l.N, url = l.Url })
                : null,
            text = body,
            stderr = string.IsNullOrWhiteSpace(page.Stderr) ? null : Cap(page.Stderr, 800),
            hint = page.Links.Count > 0 ? "op=links → op=follow link=N" : null
        }, Pretty);
    }

    bool TryTab(
        IReadOnlyDictionary<string, JsonElement> args,
        [NotNullWhen(true)] out Tab? tab,
        out string? errorJson)
    {
        tab = null;
        errorJson = null;
        EnsureMain();
        var id = SanitizeTab(Opt(args, "tab") ?? _activeTab ?? "main");
        if (!_tabs.TryGetValue(id, out tab))
        {
            errorJson = Fail("tab_missing", id, "op=scene");
            return false;
        }

        _activeTab = tab.Id;
        return true;
    }

    void EnsureMain()
    {
        lock (_gate)
            GetOrCreateUnlocked("main");
    }

    Tab GetOrCreateUnlocked(string id)
    {
        return _tabs.GetOrAdd(id, static key => new Tab(key));
    }

    static PageFetch FetchPage(string lynxExe, string url, int width, int timeoutSeconds, string userAgent)
    {
        // Chromium UA: many gates sniff Lynx/2.x and dump «update browser». Google still wants JS after that.
        var (code, stdout, stderr, ms) = RunProcessTimed(
            lynxExe,
            ["-dump", $"-width={width}", "-useragent=" + userAgent, url],
            timeoutSeconds);

        var (text, links) = SplitDumpAndLinks(stdout, url);
        return new PageFetch(url, text, links, code, stderr, ms);
    }

    static string ResolveUserAgent() => IdeSettingsHabitat.EffectiveUserAgent();

    static (string Text, IReadOnlyList<LinkRef> Links) SplitDumpAndLinks(string dump, string pageUrl)
    {
        if (string.IsNullOrEmpty(dump))
            return ("", Array.Empty<LinkRef>());

        var lines = dump.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var refIdx = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Equals("References", StringComparison.OrdinalIgnoreCase))
            {
                refIdx = i;
                break;
            }
        }

        if (refIdx < 0)
            return (dump.TrimEnd(), Array.Empty<LinkRef>());

        var text = string.Join('\n', lines.AsSpan(0, refIdx)).TrimEnd();
        var links = new List<LinkRef>();
        for (var i = refIdx + 1; i < lines.Length; i++)
        {
            var m = RefLine.Match(lines[i]);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
            var href = m.Groups[2].Value.Trim();
            if (href.Length == 0) continue;
            href = ResolveHref(pageUrl, href);
            links.Add(new LinkRef(n, href, null));
        }

        return (text, links);
    }

    static string ResolveHref(string pageUrl, string href)
    {
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return href;

        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri))
            return href;
        return Uri.TryCreate(baseUri, href, out var abs) ? abs.ToString() : href;
    }

    static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return url;
        return "https://" + url;
    }

    internal static LynxResolve ResolveLynx()
    {
        foreach (var key in new[] { "CDP_LYNX", "CDP_BROWSER_LYNX", "LYNX" })
        {
            var env = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env.Trim()))
                return LynxResolve.Found(Path.GetFullPath(env.Trim()));
        }

        var fromPath = FindOnPath("lynx.exe") ?? FindOnPath("lynx");
        if (fromPath is not null)
            return LynxResolve.Found(fromPath);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
                 {
                     Path.Combine(home, "scoop", "shims", "lynx.exe"),
                     Path.Combine(home, "scoop", "apps", "lynx", "current", "lynx.exe"),
                     Path.Combine(home, "scoop", "apps", "lynx", "current", "bin", "lynx.exe")
                 })
        {
            if (File.Exists(candidate))
                return LynxResolve.Found(candidate);
        }

        return LynxResolve.Missing(
            "lynx.exe not found on PATH / scoop shims. Install: scoop install lynx. Or set CDP_LYNX.");
    }

    static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim().Trim('"'), fileName);
                if (File.Exists(full))
                    return Path.GetFullPath(full);
            }
            catch
            {
                /* skip bad PATH entries */
            }
        }

        return null;
    }

    static (int ExitCode, string Stdout, string Stderr) RunProcess(
        string exe, IReadOnlyList<string> argv, int timeoutSeconds)
    {
        var (code, stdout, stderr, _) = RunProcessTimed(exe, argv, timeoutSeconds);
        return (code, stdout, stderr);
    }

    static (int ExitCode, string Stdout, string Stderr, int ElapsedMs) RunProcessTimed(
        string exe, IReadOnlyList<string> argv, int timeoutSeconds)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in argv)
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start " + exe);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(timeoutSeconds * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"lynx timed out after {timeoutSeconds}s");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        sw.Stop();
        return (proc.ExitCode, stdout, stderr, (int)sw.ElapsedMilliseconds);
    }

    static string Fail(string error, string? tab, string? hint) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = false,
            error,
            tab,
            hint
        }, Pretty);

    static string SanitizeTab(string id)
    {
        var s = id.Trim();
        if (s.Length == 0) return "main";
        Span<char> buf = stackalloc char[Math.Min(s.Length, 32)];
        var n = 0;
        foreach (var c in s)
        {
            if (n >= buf.Length) break;
            buf[n++] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_';
        }

        return n == 0 ? "main" : new string(buf[..n]);
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
        return null;
    }

    static string Cap(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n…(truncated)";

    static string? FirstLine(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var i = s.IndexOfAny(['\r', '\n']);
        return i < 0 ? s.Trim() : s[..i].Trim();
    }

    internal readonly record struct LynxResolve(bool Ok, string? Path, string? Error)
    {
        public static LynxResolve Found(string path) => new(true, path, null);
        public static LynxResolve Missing(string error) => new(false, null, error);
    }

    sealed record LinkRef(int N, string Url, string? Label);

    sealed record PageFetch(
        string Url,
        string Text,
        IReadOnlyList<LinkRef> Links,
        int ExitCode,
        string Stderr,
        int ElapsedMs);

    sealed class Tab(string id)
    {
        public string Id { get; } = id;
        readonly List<PageFetch> _history = [];
        int _index = -1;

        public PageFetch? Current => _index >= 0 && _index < _history.Count ? _history[_index] : null;

        public void Push(PageFetch page)
        {
            if (_index < _history.Count - 1)
                _history.RemoveRange(_index + 1, _history.Count - _index - 1);
            _history.Add(page);
            if (_history.Count > MaxHistory)
            {
                var drop = _history.Count - MaxHistory;
                _history.RemoveRange(0, drop);
                _index = _history.Count - 1;
            }
            else
                _index = _history.Count - 1;
        }

        public bool TryBack([NotNullWhen(true)] out PageFetch? page)
        {
            page = null;
            if (_index <= 0) return false;
            _index--;
            page = _history[_index];
            return true;
        }

        public bool TryForward([NotNullWhen(true)] out PageFetch? page)
        {
            page = null;
            if (_index < 0 || _index >= _history.Count - 1) return false;
            _index++;
            page = _history[_index];
            return true;
        }

        public object Card(int previewChars, string? activeTab) => new
        {
            id = Id,
            active = string.Equals(Id, activeTab, StringComparison.OrdinalIgnoreCase),
            url = Current?.Url,
            history_len = _history.Count,
            history_index = _index,
            link_count = Current?.Links.Count ?? 0,
            preview = Current is null ? null : Cap(Current.Text.Replace('\n', ' '), previewChars)
        };
    }
}
