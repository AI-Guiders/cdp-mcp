#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent session|cdp_session — session plane without Cursor MCP. go=session stays Verb.Go place-only.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteSession(string raw)
    {
        var work = NormalizeSessionCompound(raw);
        var includePack = IsTruthyKeyed(work, "include_pack")
            || IsTruthyKeyed(work, "pack");

        return new Route(
            Verb.Session,
            raw,
            Ok: true,
            Op: includePack ? "include_pack" : null,
            Go: "session");
    }

    static string NormalizeSessionCompound(string raw)
    {
        foreach (var prefix in SessionPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "session";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "session " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly string[] SessionPrefixes =
    [
        "cdp_session",
        "session_desk",
        "session_plane",
        "session"
    ];
}
