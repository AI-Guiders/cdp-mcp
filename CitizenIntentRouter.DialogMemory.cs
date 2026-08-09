#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent dialog|amnesia — Face-owned dialog history clear/scene (tip already has cdp_citizen op=clear).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteDialogMemory(string raw)
    {
        var work = NormalizeDialogMemoryCompound(raw);
        var op = ResolveDialogMemoryOp(work);
        var sticky = IsTruthyKeyed(work, "sticky")
            || IsTruthyKeyed(work, "pins")
            || work.Contains("sticky=clear", StringComparison.OrdinalIgnoreCase);

        return new Route(
            Verb.DialogMemory,
            raw,
            Ok: true,
            Op: op,
            Cmd: sticky ? "sticky" : null,
            Go: "dialog");
    }

    static string NormalizeDialogMemoryCompound(string raw)
    {
        foreach (var prefix in DialogMemoryPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return prefix.Equals("amnesia", StringComparison.OrdinalIgnoreCase)
                    || prefix.Equals("forget", StringComparison.OrdinalIgnoreCase)
                    || prefix.Equals("forget_context", StringComparison.OrdinalIgnoreCase)
                    ? "dialog clear"
                    : "dialog";
            }

            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..].TrimStart();
            if (prefix.Equals("amnesia", StringComparison.OrdinalIgnoreCase)
                || prefix.Equals("forget", StringComparison.OrdinalIgnoreCase)
                || prefix.Equals("forget_context", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(rest) ? "dialog clear" : "dialog clear " + rest;

            return "dialog " + rest;
        }

        return raw;
    }

    static string ResolveDialogMemoryOp(string work)
    {
        if (work.Equals("dialog", StringComparison.OrdinalIgnoreCase))
            return "scene";

        if (!work.StartsWith("dialog ", StringComparison.OrdinalIgnoreCase))
            return "scene";

        var rest = work["dialog ".Length..].Trim();
        if (rest.Length == 0)
            return "scene";

        var headSp = rest.IndexOf(' ');
        var head = headSp < 0 ? rest : rest[..headSp];
        return head.ToLowerInvariant() switch
        {
            "clear" or "reset" or "forget" or "amnesia" or "wipe" => "clear",
            "history" or "log" => "history",
            "scene" or "pulse" or "status" => "scene",
            _ => "scene"
        };
    }

    static readonly string[] DialogMemoryPrefixes =
    [
        "dialog_memory",
        "dialog_history",
        "citizen_dialog",
        "forget_context",
        "amnesia",
        "forget",
        "dialog"
    ];
}
