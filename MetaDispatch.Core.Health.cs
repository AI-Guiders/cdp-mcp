#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>cdp_health payload for MetaDispatch.Core (≤ADX soft-warn peel).</summary>
internal static partial class MetaDispatch
{
    static string HealthJson(MetaDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        var session = d.Session;
        var modules = d.Modules;
        var mcpVersion = d.McpVersion;
        var detail = ResolveHealthDetail(callArgs);
        var full = detail == "full";
        var jsonOpts = full ? d.Pretty : CompactHealthJson;

        var explain = TryExplainTool(d, callArgs);
        var asm = typeof(Program).Assembly;
        var exePath = Environment.ProcessPath ?? asm.Location;
        var buildUtc = TryGetExeBuildUtc(exePath);
        var pendingUpdate = TryReadPendingUpdate(exePath);

        return JsonSerializer.Serialize(new
        {
            ok = true,
            detail,
            runtime = new
            {
                version = mcpVersion,
                version_full = asm.GetName().Version?.ToString(),
                exe_path = exePath,
                build_utc = buildUtc?.ToString("o"),
                pending_update = pendingUpdate
            },
            seats = IdeOpsPulse.SeatsWire(),
            continuity = IdeIgniteArmHost.ContinuitySlice(),
            continuity_pulse = IdeIgniteArmHost.ContinuityPulseLine(),
            isolation = CdpClientWorkspace.StatusCard(),
            ops = IdeOpsPulse.Snap(),
            ops_pulse = IdeOpsPulse.Line(),
            teeth_pulse = IdeTeethChannel.PulseLine(),
            backends = modules.Select(m => new { domain = m.Domain, enabled = m.IsEnabled, health = m.HealthSummary }),
            typescript_worker = IdeLanguageTools.TsHealth(),
            lsp = IdeLanguageTools.LspHealth(resolveProbe: full),
            project = new
            {
                root = session.ProjectRoot,
                kind = session.ProjectKind,
                language = session.Language,
                solution_or_project_path = session.SolutionOrProjectPath,
                tsconfig_path = session.TsConfigPath
            },
            explain_tool = explain,
            recovery_note = full
                ? "Prefer go=deploy / cdp_deploy from the survivor seat (sibling Target). " +
                  "apply|hard|rollout: bridge holds CallTool until durable job + health (ADR-0203) — no shell escape on Not connected. " +
                  "Hard KillRunning + per-seat CDP_RELOAD_NUDGE (0.5.661; -NudgeAllSeats escape) unless -NoNudgeMcp. " +
                  "Not connected + exe still up: terminal_* Recover-CdpSeatRemount.ps1 -Seat cdp|cdp-debug (Cursor stdio zombie). " +
                  "Fallback: human Reload. Soft stages <target>.next + cdp-pending-update.json; apply with cdp_deploy mode=apply. " +
                  "Cold tools auto-warm desk bookmark once/process. Prefer cdp_health + explain_tool before guessing."
                : "pulse default — detail=full for LSP resolved_probe + long recovery_note",
            next = full
                ? null
                : new { full = "detail=full" }
        }, jsonOpts);
    }

    static readonly JsonSerializerOptions CompactHealthJson = new() { WriteIndented = false };

    /// <summary>Default pulse (no LSP path resolve). full|lsp = prior fat card.</summary>
    static string ResolveHealthDetail(IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        if (callArgs.TryGetValue("detail", out var el) && el.GetString() is { Length: > 0 } raw)
        {
            var d = raw.Trim().ToLowerInvariant();
            return d switch
            {
                "full" or "lsp" or "map" => "full",
                "pulse" or "slim" or "a" => "pulse",
                _ => "pulse"
            };
        }

        return "pulse";
    }
}
