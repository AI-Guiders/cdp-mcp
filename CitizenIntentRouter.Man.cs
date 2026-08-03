#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent man|cdp_man|manual — ops manual without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteMan(string raw)
    {
        var work = NormalizeManCompound(raw);
        var tool = ExtractKeyedValue(work, "tool")
            ?? ExtractKeyedValue(work, "name")
            ?? ExtractKeyedValue(work, "page");

        if (string.IsNullOrWhiteSpace(tool)
            && work.StartsWith("man ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = work["man ".Length..].Trim();
            if (rest.Length > 0 && !rest.Contains('=', StringComparison.Ordinal))
                tool = rest;
            else if (rest.Length > 0)
            {
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    tool = head;
            }
        }

        return new Route(
            Verb.Man,
            raw,
            Ok: true,
            Tool: string.IsNullOrWhiteSpace(tool) ? null : tool.Trim(),
            Go: "man");
    }

    static string NormalizeManCompound(string raw)
    {
        foreach (var prefix in ManPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "man";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "man " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly string[] ManPrefixes =
    [
        "cdp_man",
        "man_desk",
        "manual",
        "man"
    ];
}
