#nullable enable

namespace CdpMcp;

/// <summary>Citizen ADCM dialog memory — Prune/Partition/Persist/Rebuild (Face SoftFL; tip still cdp_citizen op=clear).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteDialogMemory(string raw)
    {
        var work = NormalizeDialogMemoryCompound(raw);
        var op = ResolveDialogMemoryOp(work);
        var sticky = IsTruthyKeyed(work, "sticky")
            || IsTruthyKeyed(work, "pins")
            || work.Contains("sticky=clear", StringComparison.OrdinalIgnoreCase);

        string? path = null;
        string? detail = null;
        if (op is "persist")
        {
            path = ExtractKeyedValue(work, "key") ?? ExtractKeyedValue(work, "k") ?? ExtractKeyedValue(work, "id");
            detail = ExtractKeyedValue(work, "value")
                ?? ExtractKeyedValue(work, "v")
                ?? ExtractKeyedValue(work, "text")
                ?? ExtractKeyedValue(work, "body");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(detail))
            {
                return new Route(
                    Verb.DialogMemory,
                    raw,
                    Ok: false,
                    Op: op,
                    Go: "dialog",
                    Reason: "persist_needs_key_and_value");
            }
        }

        return new Route(
            Verb.DialogMemory,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Detail: detail,
            Cmd: sticky ? "sticky" : null,
            Go: "dialog");
    }

    static string NormalizeDialogMemoryCompound(string raw)
    {
        foreach (var prefix in DialogMemoryPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return MapBareDialogMemoryPrefix(prefix);

            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..].TrimStart();
            return MapDialogMemoryPrefixWithRest(prefix, rest);
        }

        return raw;
    }

    static string MapBareDialogMemoryPrefix(string prefix) =>
        prefix.ToLowerInvariant() switch
        {
            "amnesia" or "forget" or "forget_context" => "dialog clear",
            "partition" or "fork" => "dialog partition",
            "persist" => "dialog persist",
            "rebuild" or "antidote" or "unpoison" => "dialog rebuild",
            "adcm" => "dialog scene",
            _ => "dialog"
        };

    static string MapDialogMemoryPrefixWithRest(string prefix, string rest)
    {
        if (string.IsNullOrWhiteSpace(rest))
            return MapBareDialogMemoryPrefix(prefix);

        return prefix.ToLowerInvariant() switch
        {
            "amnesia" or "forget" or "forget_context" => "dialog clear " + rest,
            "partition" or "fork" => "dialog partition " + rest,
            "persist" => "dialog persist " + rest,
            "rebuild" or "antidote" or "unpoison" => "dialog rebuild " + rest,
            "adcm" => "dialog " + rest,
            _ => "dialog " + rest
        };
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
            "clear" or "reset" or "forget" or "amnesia" or "wipe" or "prune" => "clear",
            "partition" or "fork" => "partition",
            "persist" or "pin" or "sticky" => "persist",
            "rebuild" or "antidote" or "unpoison" or "retract" => "rebuild",
            "history" or "log" => "history",
            "scene" or "pulse" or "status" or "adcm" => "scene",
            _ => "scene"
        };
    }

    static readonly string[] DialogMemoryPrefixes =
    [
        "dialog_memory",
        "dialog_history",
        "citizen_dialog",
        "forget_context",
        "unpoison",
        "antidote",
        "partition",
        "rebuild",
        "persist",
        "amnesia",
        "forget",
        "fork",
        "adcm",
        "dialog"
    ];
}
