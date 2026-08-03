#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent elicit|cdp_elicit — MetaDispatch cdp_elicit. No bare go=elicit organ; go=elicit stays Verb.Go.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteElicit(string raw)
    {
        var work = NormalizeElicitCompound(raw);
        var opRaw = ExtractKeyedValue(work, "op")
            ?? ExtractElicitPositionalOp(work)
            ?? "peek";
        var op = NormalizeElicitOp(opRaw.Trim().ToLowerInvariant());
        if (!IsElicitOp(op))
            return new Route(Verb.Elicit, raw, Ok: false, Reason: "elicit_op_unknown");

        return new Route(
            Verb.Elicit,
            raw,
            Ok: true,
            Op: op,
            Detail: ExtractKeyedValue(work, "message")
                ?? ExtractKeyedValue(work, "ask"),
            Go: "elicit");
    }

    static string? ExtractElicitPositionalOp(string work)
    {
        var rest = work.StartsWith("elicit ", StringComparison.OrdinalIgnoreCase)
            ? work["elicit ".Length..].Trim()
            : work;
        if (string.IsNullOrWhiteSpace(rest))
            return null;
        var token = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        if (token.Contains('=', StringComparison.Ordinal))
            return null;
        return IsElicitOp(NormalizeElicitOp(token.ToLowerInvariant())) ? token : null;
    }

    static string NormalizeElicitCompound(string raw)
    {
        foreach (var prefix in ElicitPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "elicit";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "elicit " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static string NormalizeElicitOp(string op) =>
        op switch
        {
            "caps" or "status" or "help" or "scene" => "peek",
            "form" or "create" => "ask",
            _ => op
        };

    static bool IsElicitOp(string? op) => op is "peek" or "ask";

    static readonly string[] ElicitPrefixes =
    [
        "cdp_elicit",
        "elicit"
    ];
}
