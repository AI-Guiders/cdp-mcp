using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CdpMcp;

/// <summary>Tab nav + page result helpers for InternetBrowserHabitat (lynx peel → .Lynx.cs).</summary>
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
