#nullable enable
using System.Text.Json;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent shell — sync wait ShellHabitat.Run (organ parity; not plan cmd=).</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<ShellHabitat?>? ShellHabitatResolver { get; set; }
    internal static Func<ShellCwdDefaults>? ShellDefaultsResolver { get; set; }

    /// <summary>Tests: inject fake shell_run JSON.</summary>
    internal static Func<string, string?, string?, string>? ShellRunOverride { get; set; }

    static Applied RunShell(CitizenIntentRouter.Route route)
    {
        var command = route.Command?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "shell",
                Go: "shell",
                Reason: "shell_command_required");
        }

        var tab = ExtractMcpKeyed(route.Raw, "tab");
        var cwd = ExtractMcpKeyed(route.Raw, "cwd")
            ?? ExtractMcpKeyed(route.Raw, "working_directory");

        try
        {
            string json;
            if (ShellRunOverride is { } ov)
            {
                json = ov(command, tab, cwd);
            }
            else
            {
                var habitat = ShellHabitatResolver?.Invoke();
                if (habitat is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "shell",
                        Go: "shell",
                        Reason: "no_shell");
                }

                var defaults = ShellDefaultsResolver?.Invoke()
                    ?? new ShellCwdDefaults
                    {
                        ProjectRoot = SessionResolver?.Invoke()?.ProjectRoot,
                        ScmRoot = SessionResolver?.Invoke()?.ScmRoot
                    };
                var timeout = IdeSettingsHabitat.EffectiveShellTimeout();
                json = habitat.Run(defaults, command, tab, cwd, shellPrefer: null, timeout, background: false);
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadShellPulse(json, command);
            var seat = IdeDeskSeats.PlaceOrgan("shell");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "shell",
                Seat: seat,
                Go: "shell",
                Cmd: command,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "shell_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "shell",
                Go: "shell",
                Cmd: command,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string? TryReadShellPulse(string json, string command)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());

            var bits = new List<string> { "shell" };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                bits.Add("exit=" + n);
            if (root.TryGetProperty("tab", out var t) && t.ValueKind == JsonValueKind.String
                && t.GetString() is { Length: > 0 } tab)
                bits.Add(tab);

            var tip = TruncPulse(command);
            if (tip is { Length: > 0 })
                bits.Add(tip);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("shell " + command);
        }
    }
}
