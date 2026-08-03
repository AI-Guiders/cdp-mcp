#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent cide_presentation — glass wire without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RoutePresentation(string raw)
    {
        var work = NormalizePresentationCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("cide_presentation ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("presentation ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizePresentationOp(op);

        if (!IsPresentationOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "presentation_op_unknown");

        if (op is "set" && !HasPresentationPatch(work))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "presentation_patch_required");

        return new Route(
            Verb.Presentation,
            raw,
            Ok: true,
            Op: op,
            Go: "cide_presentation");
    }

    static string NormalizePresentationCompound(string raw)
    {
        foreach (var (prefix, op) in PresentationCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "cide_presentation " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "cide_presentation" + rest;
            return "cide_presentation " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] PresentationCompounds =
    [
        ("cide_presentation_scene", "scene"),
        ("cide_presentation_set", "set"),
        ("cide_presentation_get", "scene"),
        ("presentation_set", "set"),
        ("presentation_scene", "scene")
    ];

    static string NormalizePresentationOp(string op) =>
        op switch
        {
            "get" or "status" or "desk" or "show" => "scene",
            "apply" or "patch" => "set",
            _ => op
        };

    static bool IsPresentationOp(string? op) =>
        op is "scene" or "set";

    static bool HasPresentationPatch(string work) =>
        ExtractKeyedValue(work, "topology") is { Length: > 0 }
        || ExtractKeyedValue(work, "value") is { Length: > 0 }
        || ExtractKeyedValue(work, "presentation") is { Length: > 0 }
        || ExtractKeyedValue(work, "tier") is { Length: > 0 }
        || ExtractKeyedValue(work, "pfd_primary") is { Length: > 0 }
        || ExtractKeyedValue(work, "mfd_primary") is { Length: > 0 }
        || ExtractKeyedValue(work, "pfd_status_strip") is { Length: > 0 }
        || ExtractKeyedValue(work, "forward_status_strip") is { Length: > 0 }
        || ExtractKeyedValue(work, "mfd_page") is { Length: > 0 }
        || ExtractKeyedValue(work, "page") is { Length: > 0 }
        || ExtractKeyedValue(work, "instruments") is { Length: > 0 };
}
