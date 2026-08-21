#nullable enable

using System.Text.Json;
using AIGuiders.Platform.Cockpit.Channels.EnvironmentReadiness;
using AIGuiders.Platform.Cockpit.Channels.Primitives;
using Cdp.Core;
using CdpMcp.Cockpit.Channels.EnvironmentReadiness;
using CdpMcp.Cockpit.EnvironmentReadiness;

namespace CdpMcp;

/// <summary>
/// Soft desk go=env / Meta cdp_env_readiness — headless ER snapshot for agents.
/// CIDE quarry rows + CDP habitat (backends, seat, freshness, Roslyn).
/// </summary>
internal static class IdeEnvironmentReadinessChannel
{
    public const string SchemaVersion = "environment_readiness/v1";
    public const string ToolName = "cdp_env_readiness";
    public const string GoName = "env";

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args).GetAwaiter().GetResult(),
            new JsonSerializerOptions { WriteIndented = true });

    public static async Task<string> HandleJsonAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args,
        CancellationToken ct) =>
        JsonSerializer.Serialize(await Handle(session, args, ct).ConfigureAwait(false),
            new JsonSerializerOptions { WriteIndented = true });

    public static async Task<object> Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        CancellationToken ct = default)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "pulse" or "a" => await PulseAsync(session, ct).ConfigureAwait(false),
            "rows" or "scan" => await RowsAsync(session, ct).ConfigureAwait(false),
            _ => await SceneAsync(session, ct).ConfigureAwait(false),
        };
    }

    static async Task<object> SceneAsync(SessionContext session, CancellationToken ct)
    {
        var snap = await BuildSnapshotAsync(session, ct).ConfigureAwait(false);
        return new
        {
            schema = SchemaVersion,
            op = "scene",
            row_count = snap.Rows.Count,
            worst = WorstLevel(snap.Rows),
            rows = snap.Rows.Select(ToRowDto),
        };
    }

    static async Task<object> PulseAsync(SessionContext session, CancellationToken ct)
    {
        var snap = await BuildSnapshotAsync(session, ct).ConfigureAwait(false);
        var worst = WorstLevel(snap.Rows);
        return new
        {
            schema = SchemaVersion,
            op = "pulse",
            worst = worst.ToString(),
            row_count = snap.Rows.Count,
            summary = worst is AnnunciatorLampLevel.Ok or AnnunciatorLampLevel.Advisory
                ? "ok"
                : "attention",
        };
    }

    static async Task<object> RowsAsync(SessionContext session, CancellationToken ct)
    {
        var snap = await BuildSnapshotAsync(session, ct).ConfigureAwait(false);
        return new
        {
            schema = SchemaVersion,
            op = "rows",
            rows = snap.Rows.Select(ToRowDto),
        };
    }

    static async Task<EnvironmentReadinessSnapshot> BuildSnapshotAsync(SessionContext session, CancellationToken ct)
    {
        var settings = CdpSettings.Load(null);
        var lsp = EnvironmentReadinessLspProbe.ProbeHostPresence();
        var ctx = new EnvironmentReadinessChannelContext(
            new EnvironmentReadinessSettings(settings.Memory.NotesConfig),
            session.SolutionOrProjectPath,
            lsp,
            IsMcpStdioHost: true,
            ActiveAiProvider: null);
        var input = new EnvironmentReadinessSnapshotBuilder.Input(
            ctx,
            settings.Dev,
            settings.Service,
            settings.CockpitHost);
        return await EnvironmentReadinessChannel.BuildAsync(input, ct).ConfigureAwait(false);
    }

    static object ToRowDto(AnnunciatorLampItem row) => new
    {
        id = row.Id,
        title = row.Title,
        detail = row.Detail,
        level = row.Level.ToString(),
        short_label = row.LampShortLabel,
    };

    static AnnunciatorLampLevel WorstLevel(IReadOnlyList<AnnunciatorLampItem> rows)
    {
        var worst = AnnunciatorLampLevel.Ok;
        foreach (var r in rows)
        {
            if ((int)r.Level > (int)worst)
                worst = r.Level;
        }
        return worst;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
    }
}
