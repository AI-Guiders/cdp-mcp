#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent tools|cdp_tools — shortlist palette without Cursor MCP. go=tools stays Verb.Go place-only.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteTools(string raw)
    {
        var work = NormalizeToolsCompound(raw);
        var phase = ExtractKeyedValue(work, "phase");
        var obj = ExtractKeyedValue(work, "object")
            ?? ExtractKeyedValue(work, "obj");
        var intent = ExtractKeyedValue(work, "intent");
        var language = ExtractKeyedValue(work, "language")
            ?? ExtractKeyedValue(work, "lang");
        var limit = ExtractKeyedValue(work, "limit");

        return new Route(
            Verb.Tools,
            raw,
            Ok: true,
            Scene: string.IsNullOrWhiteSpace(phase) ? null : phase.Trim(),
            Organ: string.IsNullOrWhiteSpace(obj) ? null : obj.Trim(),
            Detail: string.IsNullOrWhiteSpace(intent) ? null : intent.Trim(),
            Tool: string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
            Cmd: string.IsNullOrWhiteSpace(limit) ? null : limit.Trim(),
            Go: "tools");
    }

    static string NormalizeToolsCompound(string raw)
    {
        foreach (var prefix in ToolsPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "tools";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "tools " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly string[] ToolsPrefixes =
    [
        "cdp_tools",
        "tools_desk",
        "tools_palette",
        "palette",
        "tools"
    ];
}
