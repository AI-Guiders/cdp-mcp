#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Anchor Start/Stop — operator GUI cockpit host (ADR-0019 companion).
/// Meta <c>cdp_cockpit_host</c> / <c>go=cockpit_start|cockpit_stop</c>.
/// Config SSOT: <c>[cockpit_host] exe</c> in cdp-mcp.toml (process layer).
/// Start <c>path=</c> overrides once; env <c>CDP_COCKPIT_HOST_EXE</c> is escape only.
/// Runtime latch: in-proc + OS rediscover by exe path (no sidecar JSON).
/// Does not mutate Intent Melody / CascadeIdeSettings.
/// </summary>
internal static partial class IdeCockpitHostChannel
{
    public const string SchemaVersion = "cockpit_host/v1";
    public const string ToolName = "cdp_cockpit_host";
    public const string EnvExe = "CDP_COCKPIT_HOST_EXE";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly object Gate = new();
    static CockpitHostSettings _cfg = new();
    static HostState? _live;

    /// <summary>Legacy stub path — deleted on Configure so remounts do not revive JSON latch.</summary>
    public static string LegacyStatePath => Path.Combine(CdpProfile.StateRoot, "cockpit-host.json");

    public static void Configure(CockpitHostSettings settings)
    {
        _cfg = settings ?? new CockpitHostSettings();
        TryDeleteLegacyJson();
    }

    public static string HandleJson(IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(args), JsonOpts);

    /// <summary>Shared pulse for ICM / host scenes (no JSON round-trip).</summary>
    public static CockpitHostProfile.Snapshot GetHostPulse()
    {
        var st = Snapshot();
        return st is null
            ? new CockpitHostProfile.Snapshot("down", "agent-only", null)
            : new CockpitHostProfile.Snapshot("up", "dual-cockpit", st.Pid);
    }

    public static object Handle(IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "start" or "up" or "open" => Start(args),
            "stop" or "down" or "close" => Stop(),
            _ => Scene()
        };
    }

    static object Scene()
    {
        var st = Snapshot();
        return new
        {
            ok = true,
            schema = SchemaVersion,
            tool = ToolName,
            op = "scene",
            pulse = st is not null
                ? $"cockpit_host · up · pid={st.Pid}"
                : "cockpit_host · down · agent-only",
            gui_host = st is not null ? "up" : "down",
            host_profile = st is not null ? "dual-cockpit" : "agent-only",
            pid = st?.Pid,
            exe = st?.Exe,
            started_utc = st?.StartedUtc,
            exe_configured = ResolveExe(null) is not null,
            config_source = ConfigSourceLabel(),
            env_escape = EnvExe,
            hint = st is not null
                ? "op=stop to close GUI; MCP/ICM keep running."
                : "op=start path=… or [cockpit_host] exe in cdp-mcp.toml (env CDP_COCKPIT_HOST_EXE = escape). Melody/settings load with shell — do not strip them."
        };
    }


    sealed class HostState
    {
        public int Pid { get; set; }
        public string? Exe { get; set; }
        public string? StartedUtc { get; set; }
    }
}
