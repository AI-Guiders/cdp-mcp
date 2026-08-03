#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent context|cdp_context — session phase/object without Cursor MCP. go=context stays Verb.Go place-only.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteContext(string raw)
    {
        var work = NormalizeContextCompound(raw);
        var phase = ExtractKeyedValue(work, "phase");
        var obj = ExtractKeyedValue(work, "object")
            ?? ExtractKeyedValue(work, "obj");
        var intent = ExtractKeyedValue(work, "intent");
        var language = ExtractKeyedValue(work, "language")
            ?? ExtractKeyedValue(work, "lang");
        var get = ExtractKeyedValue(work, "get");
        var hold = ExtractKeyedValue(work, "layout_hold")
            ?? ExtractKeyedValue(work, "hold");

        return new Route(
            Verb.Context,
            raw,
            Ok: true,
            Scene: string.IsNullOrWhiteSpace(phase) ? null : phase.Trim(),
            Organ: string.IsNullOrWhiteSpace(obj) ? null : obj.Trim(),
            Detail: string.IsNullOrWhiteSpace(intent) ? null : intent.Trim(),
            Tool: string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
            Op: IsTruthy(get) ? "get" : null,
            Cmd: IsTruthy(hold) ? "layout_hold" : null,
            Go: "context");
    }

    static string NormalizeContextCompound(string raw)
    {
        foreach (var prefix in ContextPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "context";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "context " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    internal static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTruthyKeyed(string raw, string key) =>
        IsTruthy(ExtractKeyedValue(raw, key));

    static readonly string[] ContextPrefixes =
    [
        "cdp_context",
        "context_desk",
        "session_context",
        "context"
    ];
}
