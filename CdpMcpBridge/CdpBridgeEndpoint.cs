using System.Text.Json;
using Tomlyn;

namespace CdpMcpBridge;

/// <summary>
/// Hot-standby endpoint resolution (ADR-0203+): the active service port comes from
/// the bridge's own config toml ([service] port). A/B deploy flips by rewriting that
/// toml; bridges re-read it on mtime change and drift to the new slot — no restarts,
/// no connection gaps.
/// </summary>
internal static class CdpBridgeEndpoint
{
    static readonly object Gate = new();
    static Uri _configured = new("http://127.0.0.1:8771/");
    static string _configPath = "";
    static DateTime _configUtc;
    static int _activePort;

    /// <summary>Bind to the configured base + config watch path. Called once at startup.</summary>
    public static void Init(Uri configuredBase, string? bridgeConfigPath)
    {
        lock (Gate)
        {
            _configured = configuredBase;
            _configPath = bridgeConfigPath ?? "";
            _configUtc = File.Exists(_configPath)
                ? File.GetLastWriteTimeUtc(_configPath)
                : DateTime.MinValue;
            _activePort = 0;
        }
    }

    /// <summary>
    /// Current active base address. Re-parses the bridge config toml when its mtime
    /// changes (2-second cache); falls back to the configured base on any error.
    /// </summary>
    public static Uri Current()
    {
        lock (Gate)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_configPath) && File.Exists(_configPath))
                {
                    var mtime = File.GetLastWriteTimeUtc(_configPath);
                    if (mtime != _configUtc)
                    {
                        _configUtc = mtime;
                        _activePort = ReadPortFromToml(_configPath);
                    }
                }
            }
            catch
            {
                /* config unreadable — keep last known */
            }

            return _activePort > 0
                ? new Uri($"http://127.0.0.1:{_activePort}/")
                : _configured;
        }
    }

    static int ReadPortFromToml(string path)
    {
        try
        {
            var doc = TomlSerializer.Deserialize<BridgeTomlDocument>(
                File.ReadAllText(path),
                new TomlSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var port = doc?.Service?.Port;
            return port is > 0 and < 65536 ? port.Value : 0;
        }
        catch
        {
            return 0;
        }
    }
}
