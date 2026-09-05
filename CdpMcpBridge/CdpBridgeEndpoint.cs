using System.Net.Http;
using System.Net.Http.Json;

namespace CdpMcpBridge;

/// <summary>
/// Hot-standby endpoint resolution — INotifyPropertyChanged-style (ADR-0203+):
/// the bridge OBSERVES both service slots' /healthz (configured port + reserve
/// port), reads their build fingerprints, and prefers the freshest healthy slot.
/// Deploys bring the idle slot up with fresh bits — bridges drift automatically.
/// No config edits, no flips, no connection gaps.
/// </summary>
internal static class CdpBridgeEndpoint
{
    static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(2) };
    static readonly object Gate = new();
    static Uri _configured = new("http://127.0.0.1:8771/");
    static int _reservePort;
    static DateTimeOffset _lastProbeUtc = DateTimeOffset.MinValue;
    static Uri _preferred = new("http://127.0.0.1:8771/");
    static int _probeInFlight;

    /// <summary>Bind slots. Called once at startup. Reserve = warm standby port.</summary>
    public static void Init(Uri configuredBase, int reservePort)
    {
        lock (Gate)
        {
            _configured = configuredBase;
            _reservePort = reservePort;
            _preferred = configuredBase;
        }
    }

    /// <summary>
    /// Current preferred slot base. Cached; a background probe refresh fires when
    /// the cache is older than 5 seconds — callers never block on healthz.
    /// </summary>
    public static Uri Current()
    {
        var stale = false;
        lock (Gate)
            stale = DateTimeOffset.UtcNow - _lastProbeUtc > TimeSpan.FromSeconds(5);
        if (stale)
        {
            lock (Gate)
                if (DateTimeOffset.UtcNow - _lastProbeUtc > TimeSpan.FromSeconds(4)
                    && _probeInFlight == 0)
                {
                    _probeInFlight = 1;
                    _lastProbeUtc = DateTimeOffset.UtcNow;
                    _ = Task.Run(() => _ = ProbeBothAsync());
                }
        }

        lock (Gate)
            return _preferred;
    }

    static async Task ProbeBothAsync()
    {
        try
        {
            var a = await ProbeAsync(_configured).ConfigureAwait(false);
            var b = _reservePort is > 0 and < 65536
                ? await ProbeAsync(new Uri($"http://127.0.0.1:{_reservePort}/")).ConfigureAwait(false)
                : null;

            Uri preferred;
            if (a is { Healthy: true } && b is { Healthy: true })
                preferred = a.BuildUtc >= b.BuildUtc ? a.Base : b.Base;
            else if (a is { Healthy: true })
                preferred = a.Base;
            else if (b is { Healthy: true })
                preferred = b.Base;
            else
                preferred = _configured;

            lock (Gate)
                _preferred = preferred;
        }
        catch
        {
            /* probes fail during deploy gaps — keep last known */
        }
        finally
        {
            lock (Gate)
            {
                _probeInFlight = 0;
                _lastProbeUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    sealed record SlotState(Uri Base, bool Healthy, DateTimeOffset BuildUtc);

    static async Task<SlotState?> ProbeAsync(Uri baseUri)
    {
        try
        {
            using var resp = await Probe.GetAsync(new Uri(baseUri, "healthz")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>().ConfigureAwait(false);
            var utc = DateTimeOffset.MinValue;
            if (json.TryGetProperty("build_utc", out var bu) && bu.ValueKind == System.Text.Json.JsonValueKind.String)
                DateTimeOffset.TryParse(bu.GetString(), out utc);
            else if (json.TryGetProperty("build", out var b)
                     && b.ValueKind == System.Text.Json.JsonValueKind.Object
                     && b.TryGetProperty("time", out var bt)
                     && bt.ValueKind == System.Text.Json.JsonValueKind.String)
                DateTimeOffset.TryParse(bt.GetString(), out utc);
            return new SlotState(baseUri, true, utc);
        }
        catch
        {
            return null;
        }
    }
}
