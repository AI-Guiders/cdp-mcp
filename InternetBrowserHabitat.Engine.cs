using System.Diagnostics;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CdpMcp;

/// <summary>Tab nav + lynx fetch/process helpers for InternetBrowserHabitat (soft-warn peel).</summary>
internal sealed partial class InternetBrowserHabitat
{
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
