#nullable enable
using System.Text.Json;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent shell — habitat Scene|Which|… + sync Run (organ parity SoftFL).</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<ShellHabitat?>? ShellHabitatResolver { get; set; }
    internal static Func<ShellCwdDefaults>? ShellDefaultsResolver { get; set; }

    /// <summary>Tests: inject fake shell_run JSON.</summary>
    internal static Func<string, string?, string?, string>? ShellRunOverride { get; set; }

    /// <summary>Tests: inject fake habitat-op JSON (scene|which|…).</summary>
    internal static Func<string, string?, string>? ShellOrganOverride { get; set; }

    static Applied RunShell(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? null : route.Op.Trim().ToLowerInvariant();
        if (op is { Length: > 0 })
            return RunShellOrgan(route, op);

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

    static Applied RunShellOrgan(CitizenIntentRouter.Route route, string op)
    {
        var tab = ExtractMcpKeyed(route.Raw, "tab");
        try
        {
            string json;
            if (ShellOrganOverride is { } ov)
            {
                json = ov(op, tab);
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

                json = DispatchShellOrgan(habitat, op, tab, route.Raw);
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadShellPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("shell");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "shell",
                Seat: seat,
                Go: "shell",
                Cmd: op,
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
                Cmd: op,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string DispatchShellOrgan(ShellHabitat habitat, string op, string? tab, string raw)
    {
        switch (op)
        {
            case "scene":
                return habitat.Scene();
            case "which":
                return habitat.Which(tab);
            case "history":
            {
                var n = 20;
                if (ExtractMcpKeyed(raw, "n") is { Length: > 0 } nRaw
                    && int.TryParse(nRaw, out var nn))
                    n = nn;
                return habitat.History(tab, n);
            }
            case "last":
            {
                var maxChars = 0;
                if (ExtractMcpKeyed(raw, "max_chars") is { Length: > 0 } mcRaw
                    && int.TryParse(mcRaw, out var mc))
                    maxChars = mc;
                return habitat.Last(tab, maxChars);
            }
            case "rerun":
            {
                var defaults = ShellDefaultsResolver?.Invoke()
                    ?? new ShellCwdDefaults
                    {
                        ProjectRoot = SessionResolver?.Invoke()?.ProjectRoot,
                        ScmRoot = SessionResolver?.Invoke()?.ScmRoot
                    };
                int? index = null;
                if (ExtractMcpKeyed(raw, "index") is { Length: > 0 } ixRaw
                    && int.TryParse(ixRaw, out var ix))
                    index = ix;
                var timeout = IdeSettingsHabitat.EffectiveShellTimeout();
                return habitat.Rerun(defaults, tab, index, timeout, background: false);
            }
            case "kill":
                return habitat.Kill(tab);
            case "close":
                return habitat.Close(tab);
            default:
                throw new ArgumentException("shell_op_unknown: " + op);
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
            if (root.TryGetProperty("schema", out var sch) && sch.ValueKind == JsonValueKind.String
                && sch.GetString() is { Length: > 0 } schema)
            {
                // shell_scene/v0 → scene tip for habitat verbs
                var slash = schema.IndexOf('/');
                var head = slash < 0 ? schema : schema[..slash];
                if (head.StartsWith("shell_", StringComparison.OrdinalIgnoreCase)
                    && head.Length > "shell_".Length)
                    bits.Add(head["shell_".Length..]);
            }

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
