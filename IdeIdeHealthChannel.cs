#nullable enable

using System.Text.Json;
using AIGuiders.Platform.Cockpit.Channels.IdeHealth;
using Cdp.Core;
using CdpMcp.Cockpit.IdeHealth;

namespace CdpMcp;

/// <summary>
/// Soft desk go=ide_health / Meta cdp_ide_health — headless IDE Health strip fold for agents.
/// Platform CCU quarry + CDP habitat probes (git, test/debug latches, LSP).
/// </summary>
internal static class IdeIdeHealthChannel
{
    public const string SchemaVersion = "ide_health/v1";
    public const string ToolName = "cdp_ide_health";
    public const string GoName = "ide_health";

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args),
            new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "pulse" or "a" => Pulse(session),
            "segments" or "strip" => Segments(session),
            _ => Scene(session),
        };
    }

    static object Scene(SessionContext session)
    {
        var snap = IdeHealthSnapshotHost.BuildOutput(session);
        return new
        {
            schema = SchemaVersion,
            op = "scene",
            segment_count = snap.Segments.Count,
            lsp_hint = snap.IdeHost.LspStatusHint,
            segments = snap.Segments.Select(ToDto),
        };
    }

    static object Pulse(SessionContext session)
    {
        var snap = IdeHealthSnapshotHost.BuildOutput(session);
        var building = snap.Segments.FirstOrDefault(s => s.IsBuildSource)?.IsBuildRunning == true;
        return new
        {
            schema = SchemaVersion,
            op = "pulse",
            building,
            summary = string.Join(" · ", snap.Segments.Select(s => s.CockpitShort)),
            lsp_hint = snap.IdeHost.LspStatusHint,
        };
    }

    static object Segments(SessionContext session)
    {
        var snap = IdeHealthSnapshotHost.BuildOutput(session);
        return new
        {
            schema = SchemaVersion,
            op = "segments",
            segments = snap.Segments.Select(ToDto),
        };
    }

    static object ToDto(IdeHealthSegment seg) => new
    {
        source = seg.Source.ToString().ToLowerInvariant(),
        stratum = seg.Stratum.ToString().ToLowerInvariant(),
        scope = seg.Scope.ToString().ToLowerInvariant(),
        project_path = seg.ProjectPath,
        line = seg.LineText,
        short_label = seg.CockpitShort,
        is_build_running = seg.IsBuildRunning,
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
    }
}
