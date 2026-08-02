#nullable enable

namespace CdpMcp;

internal static partial class CitizenIntentRouter
{
    static Route RouteShell(string raw)
    {
        var command = ExtractKeyedValue(raw, "command");
        if (string.IsNullOrWhiteSpace(command)
            && raw.StartsWith("shell ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw["shell ".Length..].Trim();
            // Drop leading optional kv that aren't the command body.
            while (rest.StartsWith("tab=", StringComparison.OrdinalIgnoreCase)
                   || rest.StartsWith("cwd=", StringComparison.OrdinalIgnoreCase)
                   || rest.StartsWith("working_directory=", StringComparison.OrdinalIgnoreCase))
            {
                var sp = rest.IndexOf(' ');
                if (sp < 0)
                {
                    rest = "";
                    break;
                }

                rest = rest[(sp + 1)..].Trim();
            }

            command = rest;
        }

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
}
