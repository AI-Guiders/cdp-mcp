#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent shell — habitat verbs (scene|which|…) + Run command (browser-parity SoftFL).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteShell(string raw)
    {
        var op = ExtractKeyedValue(raw, "op") ?? ExtractKeyedValue(raw, "cmd");
        if (string.IsNullOrWhiteSpace(op)
            && raw.StartsWith("shell ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = StripShellLeadingKv(raw["shell ".Length..].Trim());
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0
                && !head.Contains('=', StringComparison.Ordinal)
                && IsShellHabitatOp(head))
                op = head;
        }

        if (string.IsNullOrWhiteSpace(op)
            && (raw.Equals("shell", StringComparison.OrdinalIgnoreCase)
                || StripShellLeadingKv(
                    raw.StartsWith("shell ", StringComparison.OrdinalIgnoreCase)
                        ? raw["shell ".Length..].Trim()
                        : "").Length == 0))
            op = "scene";

        if (!string.IsNullOrWhiteSpace(op))
        {
            op = op.Trim().ToLowerInvariant();
            if (IsShellHabitatOp(op))
            {
                return new Route(
                    Verb.Shell,
                    raw,
                    Ok: true,
                    Op: op,
                    Go: "shell");
            }
        }

        var command = ExtractShellCommand(raw);
        if (string.IsNullOrWhiteSpace(command))
        {
            return new Route(
                Verb.Shell,
                raw,
                Ok: false,
                Go: "shell",
                Reason: "shell_command_required");
        }

        return new Route(
            Verb.Shell,
            raw,
            Ok: true,
            Command: command.Trim(),
            Go: "shell");
    }

    static bool IsShellHabitatOp(string? s) =>
        s is "scene" or "which" or "last" or "history" or "rerun" or "kill" or "close";

    /// <summary>command= takes rest-of-line (until tab=|cwd=|working_directory=); free form after shell.</summary>
    static string? ExtractShellCommand(string raw)
    {
        var needle = "command=";
        var idx = raw.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var i = idx + needle.Length;
            if (i >= raw.Length)
                return "";
            if (raw[i] == '"')
            {
                var end = raw.IndexOf('"', i + 1);
                return end < 0 ? raw[(i + 1)..] : raw[(i + 1)..end];
            }

            var rest = raw[i..];
            var cut = IndexOfShellTrailingKv(rest);
            return (cut < 0 ? rest : rest[..cut]).Trim();
        }

        if (!raw.StartsWith("shell ", StringComparison.OrdinalIgnoreCase))
            return null;

        return StripShellLeadingKv(raw["shell ".Length..].Trim());
    }

    static string StripShellLeadingKv(string rest)
    {
        while (rest.StartsWith("tab=", StringComparison.OrdinalIgnoreCase)
               || rest.StartsWith("cwd=", StringComparison.OrdinalIgnoreCase)
               || rest.StartsWith("working_directory=", StringComparison.OrdinalIgnoreCase)
               || rest.StartsWith("op=", StringComparison.OrdinalIgnoreCase)
               || rest.StartsWith("cmd=", StringComparison.OrdinalIgnoreCase))
        {
            var sp = rest.IndexOf(' ');
            if (sp < 0)
                return "";
            rest = rest[(sp + 1)..].Trim();
        }

        return rest;
    }

    static int IndexOfShellTrailingKv(string rest)
    {
        var best = -1;
        foreach (var key in new[] { " tab=", " cwd=", " working_directory=" })
        {
            var at = rest.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (best < 0 || at < best))
                best = at;
        }

        return best;
    }
}
