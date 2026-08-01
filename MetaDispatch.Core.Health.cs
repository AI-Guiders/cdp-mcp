#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>cdp_health payload for MetaDispatch.Core (≤ADX soft-warn peel).</summary>
internal static partial class MetaDispatch
{
    static string HealthJson(MetaDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        var session = d.Session;
        var byDomain = d.ByDomain;
        var modules = d.Modules;
        var allAffordances = d.AllAffordances;
        var mcpVersion = d.McpVersion;
        var Pretty = d.Pretty;

        object? explain = null;
        if (callArgs.TryGetValue("explain_tool", out var eht) && eht.GetString() is { Length: > 0 } en)
            explain = SessionPlane.ExplainTool(en, session, byDomain, allAffordances);

        var asm = typeof(Program).Assembly;
        var exePath = Environment.ProcessPath ?? asm.Location;
        DateTimeOffset? buildUtc = null;
        try
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                buildUtc = File.GetLastWriteTimeUtc(exePath);
        }
        catch { /* ignore */ }

        object? pendingUpdate = null;
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (dir is { Length: > 0 })
            {
                var pendingPath = Path.Combine(dir, "cdp-pending-update.json");
                if (File.Exists(pendingPath))
                    pendingUpdate = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(pendingPath));
            }
        }
        catch
        {
            pendingUpdate = new { ok = false, error = "pending_update_unreadable" };
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
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
            lsp = IdeLanguageTools.LspHealth(),
            project = new
            {
                root = session.ProjectRoot,
                kind = session.ProjectKind,
                language = session.Language,
                solution_or_project_path = session.SolutionOrProjectPath,
                tsconfig_path = session.TsConfigPath
            },
            explain_tool = explain,
            recovery_note =
                "Prefer go=deploy / cdp_deploy from the survivor seat (sibling Target). " +
                "Hard KillRunning + CDP_RELOAD_NUDGE (kj-1349) unless -NoNudgeMcp. " +
                "Fallback: human Reload. Soft stages <target>.next + cdp-pending-update.json. " +
                "Cold tools auto-warm desk bookmark once/process. Prefer cdp_health + explain_tool before guessing."
        }, Pretty);
    }
}
