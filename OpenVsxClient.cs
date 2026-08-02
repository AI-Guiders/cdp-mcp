#nullable enable
using System.Net.Http;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Open VSX registry client — search + download VSIX.
/// Base: https://open-vsx.org (override CDP_OPENVSX_BASE for tests).
/// Agent does not unpack; CDP quarantine installs the downloaded file.
/// </summary>
internal static partial class OpenVsxClient
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

}
