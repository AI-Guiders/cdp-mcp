#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent sa_desk|cdp_sa|code_sa — pre-refactor SA without Cursor MCP. go=sa stays Verb.Go (EICAS); not stolen.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteSa(string raw)
    {
        var work = NormalizeSaCompound(raw);
        var depthKeyed = ExtractKeyedValue(work, "depth")
            ?? ExtractKeyedValue(work, "shape");
        var positional = ExtractSaPositionalToken(work);
        string? depth;
        string? locusFromPos = null;
        if (!string.IsNullOrWhiteSpace(depthKeyed))
        {
            depth = NormalizeSaDepth(depthKeyed.Trim().ToLowerInvariant());
            if (!IsSaDepth(depth))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: "sa_depth_unknown");
        }
        else if (positional is { Length: > 0 } tok)
        {
            var norm = NormalizeSaDepth(tok.ToLowerInvariant());
            if (IsSaDepth(norm))
                depth = norm;
            else
            {
                depth = "slim";
                locusFromPos = tok;
            }
        }
        else
        {
            depth = "slim";
        }

        var locus = ExtractKeyedValue(work, "locus")
            ?? ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "focus")
            ?? locusFromPos;
        var scope = ExtractKeyedValue(work, "scope");

        return new Route(
            Verb.Sa,
            raw,
            Ok: true,
            Op: depth,
            Path: string.IsNullOrWhiteSpace(locus) ? null : locus.Trim().Trim('"'),
            Detail: string.IsNullOrWhiteSpace(scope) ? null : scope.Trim(),
            Go: "sa_desk");
    }

    static string? ExtractSaPositionalToken(string work)
    {
        var rest = work.StartsWith("sa ", StringComparison.OrdinalIgnoreCase)
            ? work["sa ".Length..].Trim()
            : work;
        if (string.IsNullOrWhiteSpace(rest) || rest.Contains('=', StringComparison.Ordinal))
            return null;
        return rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0]
            .Trim().Trim('"');
    }

    static string NormalizeSaCompound(string raw)
    {
        foreach (var prefix in SaPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "sa";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "sa " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static string NormalizeSaDepth(string depth) =>
        depth switch
        {
            "desk" or "status" or "a" => "slim",
            "detail" or "wide" => "full",
            "p" => "pulse",
            _ => depth
        };

    static bool IsSaDepth(string? depth) =>
        depth is "slim" or "full" or "pulse";

    static readonly string[] SaPrefixes =
    [
        "cdp_sa",
        "sa_desk",
        "code_sa",
        "pre_sa",
        "sa_code",
        "sa"
    ];
}
