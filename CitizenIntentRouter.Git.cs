#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent git — in-proc git soft organ e2e (observe + commit/push); not shell porcelain.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteGit(string raw)
    {
        var tool = ExtractKeyedValue(raw, "tool") ?? ExtractKeyedValue(raw, "op");
        if (string.IsNullOrWhiteSpace(tool) && raw.StartsWith("git ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw["git ".Length..].Trim();
            var sp = rest.IndexOf(' ');
            var head = sp < 0 ? rest : rest[..sp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
            {
                if (!IsGitToolHead(head))
                    return new Route(Verb.Unknown, raw, Ok: false, Reason: "git_tool_unknown");
                tool = head;
            }
        }

        tool = string.IsNullOrWhiteSpace(tool) ? "scene" : tool.Trim().ToLowerInvariant();
        tool = NormalizeGitTool(tool);

        if (!IsGitTool(tool))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "git_tool_unknown");

        if (tool is "show" && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "rev")))
        {
            return new Route(
                Verb.Git,
                raw,
                Ok: false,
                Op: tool,
                Go: "git",
                Reason: "git_rev_required");
        }

        if (tool is "commit" && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "message")))
        {
            return new Route(
                Verb.Git,
                raw,
                Ok: false,
                Op: tool,
                Go: "git",
                Reason: "git_message_required");
        }

        return new Route(
            Verb.Git,
            raw,
            Ok: true,
            Op: tool,
            Go: "git");
    }

    static string NormalizeGitTool(string tool) =>
        tool switch
        {
            "scm" or "map" => "scene",
            "st" or "status" or "porcelain" => "scene",
            "diffscene" or "diffs" => "diff_scene",
            "pf" => "preflight",
            "ci" => "commit",
            _ => tool
        };

    static bool IsGitToolHead(string? head)
    {
        if (string.IsNullOrWhiteSpace(head))
            return false;
        return IsGitTool(NormalizeGitTool(head.Trim().ToLowerInvariant()));
    }

    /// <summary>Observe + ship path. Branch/submodule/preflight_fix_safe stay out (thicker risk).</summary>
    static bool IsGitTool(string? tool) =>
        tool is "scene" or "diff" or "diff_scene" or "log" or "show" or "preflight" or "plan"
            or "commit" or "push" or "pull" or "fetch";
}
