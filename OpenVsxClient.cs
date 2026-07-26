#nullable enable
using System.Net.Http;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Open VSX registry client — search + download VSIX.
/// Base: https://open-vsx.org (override CDP_OPENVSX_BASE for tests).
/// Agent does not unpack; CDP quarantine installs the downloaded file.
/// </summary>
internal static class OpenVsxClient
{
    public const int DefaultSize = 12;
    public const int MaxSize = 40;

    static readonly object HttpGate = new();
    static HttpClient? SharedHttp;

    /// <summary>Tests: inject handler before first Search/Download (cleared in finally).</summary>
    internal static HttpMessageHandler? TestHandler;

    static HttpClient Http
    {
        get
        {
            lock (HttpGate)
            {
                if (SharedHttp is null || !ReferenceEquals(_boundHandler, TestHandler))
                {
                    SharedHttp?.Dispose();
                    SharedHttp = CreateHttp();
                }

                return SharedHttp;
            }
        }
    }

    static HttpMessageHandler? _boundHandler;

    static HttpClient CreateHttp()
    {
        HttpMessageHandler handler = TestHandler ?? new HttpClientHandler();
        _boundHandler = TestHandler;
        var http = new HttpClient(handler, disposeHandler: TestHandler is null)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("cdp-mcp-plugins/0.5 (+open-vsx)");
        return http;
    }

    /// <summary>Reset shared client after TestHandler change.</summary>
    internal static void ResetHttpForTests()
    {
        lock (HttpGate)
        {
            SharedHttp?.Dispose();
            SharedHttp = null;
            _boundHandler = null;
        }
    }

    public sealed record Hit(
        string Namespace,
        string Name,
        string Version,
        string? DisplayName,
        string? Description,
        string? DownloadUrl)
    {
        public string Id => Namespace + "." + Name;
    }

    public sealed record SearchResult(
        bool Ok,
        string? Error,
        string? Hint,
        string Query,
        IReadOnlyList<Hit> Hits);

    public sealed record DownloadResult(
        bool Ok,
        string? Error,
        string? Hint,
        string? Path,
        Hit? Meta);

    public static string BaseUrl
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("CDP_OPENVSX_BASE");
            if (env is { Length: > 0 })
                return env.TrimEnd('/');
            return "https://open-vsx.org";
        }
    }

    public static bool TryParseId(string? raw, out string ns, out string name)
    {
        ns = "";
        name = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim();
        if (s.StartsWith("openvsx:", StringComparison.OrdinalIgnoreCase))
            s = s["openvsx:".Length..].Trim();

        var slash = s.IndexOf('/');
        if (slash > 0 && slash < s.Length - 1)
        {
            ns = s[..slash].Trim();
            name = s[(slash + 1)..].Trim();
            return ns.Length > 0 && name.Length > 0;
        }

        var dot = s.IndexOf('.');
        if (dot > 0 && dot < s.Length - 1)
        {
            ns = s[..dot].Trim();
            name = s[(dot + 1)..].Trim();
            return ns.Length > 0 && name.Length > 0;
        }

        return false;
    }

    public static SearchResult Search(string query, int size = DefaultSize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResult(false, "query_required", "q= or query= search text", "", []);

        size = Math.Clamp(size, 1, MaxSize);
        var q = query.Trim();
        try
        {
            var url = BaseUrl + "/api/-/search?query=" + Uri.EscapeDataString(q) + "&size=" + size;
            using var resp = Http.GetAsync(url, cancellationToken).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                return new SearchResult(
                    false,
                    "search_http_" + (int)resp.StatusCode,
                    Trunc(body, 200),
                    q,
                    []);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("extensions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return new SearchResult(false, "search_bad_payload", "no extensions[]", q, []);

            var hits = new List<Hit>();
            foreach (var el in arr.EnumerateArray())
            {
                var ns = Prop(el, "namespace") ?? "";
                var name = Prop(el, "name") ?? "";
                if (ns.Length == 0 || name.Length == 0)
                    continue;
                hits.Add(new Hit(
                    ns,
                    name,
                    Prop(el, "version") ?? "?",
                    Prop(el, "displayName") ?? Prop(el, "name"),
                    Trunc(Prop(el, "description"), 120),
                    null));
            }

            return new SearchResult(true, null, null, q, hits);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SearchResult(false, "search_failed", Trunc(ex.Message, 240), q, []);
        }
    }

    public static DownloadResult Download(
        string ns,
        string name,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(name))
            return new DownloadResult(false, "id_required", "id=publisher.name or namespace/name", null, null);

        ns = ns.Trim();
        name = name.Trim();
        try
        {
            var metaUrl = version is { Length: > 0 }
                ? BaseUrl + "/api/" + Uri.EscapeDataString(ns) + "/" + Uri.EscapeDataString(name) + "/" + Uri.EscapeDataString(version)
                : BaseUrl + "/api/" + Uri.EscapeDataString(ns) + "/" + Uri.EscapeDataString(name) + "/latest";

            using var metaResp = Http.GetAsync(metaUrl, cancellationToken).GetAwaiter().GetResult();
            var metaBody = metaResp.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            if (!metaResp.IsSuccessStatusCode)
            {
                return new DownloadResult(
                    false,
                    "resolve_http_" + (int)metaResp.StatusCode,
                    Trunc(metaBody, 200) ?? "extension not on Open VSX",
                    null,
                    null);
            }

            using var metaDoc = JsonDocument.Parse(metaBody);
            var root = metaDoc.RootElement;
            var ver = Prop(root, "version") ?? version ?? "?";
            var display = Prop(root, "displayName") ?? name;
            string? download = null;
            if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Object)
                download = Prop(files, "download");

            if (download is not { Length: > 0 })
            {
                download = BaseUrl + "/api/" + Uri.EscapeDataString(ns) + "/" + Uri.EscapeDataString(name) + "/"
                    + Uri.EscapeDataString(ver) + "/file/" + Uri.EscapeDataString(ns) + "."
                    + Uri.EscapeDataString(name) + "-" + Uri.EscapeDataString(ver) + ".vsix";
            }

            var hit = new Hit(ns, name, ver, display, Trunc(Prop(root, "description"), 120), download);
            var destDir = Path.Combine(Path.GetTempPath(), "cdp-ovsx-dl");
            Directory.CreateDirectory(destDir);
            var dest = Path.Combine(destDir, ns + "." + name + "-" + ver + ".vsix");

            using var dlResp = Http.GetAsync(download, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .GetAwaiter().GetResult();
            if (!dlResp.IsSuccessStatusCode)
            {
                var err = dlResp.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                return new DownloadResult(
                    false,
                    "download_http_" + (int)dlResp.StatusCode,
                    Trunc(err, 200),
                    null,
                    hit);
            }

            using (var stream = dlResp.Content.ReadAsStream(cancellationToken))
            using (var fs = File.Create(dest))
                stream.CopyTo(fs);

            if (!File.Exists(dest) || new FileInfo(dest).Length < 64)
                return new DownloadResult(false, "download_empty", "vsix too small", dest, hit);

            return new DownloadResult(true, null, null, dest, hit);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DownloadResult(false, "download_failed", Trunc(ex.Message, 240), null, null);
        }
    }

    static string? Prop(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    static string? Trunc(string? s, int max)
    {
        if (s is null) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
