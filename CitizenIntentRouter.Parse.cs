#nullable enable

namespace CdpMcp;
internal static partial class CitizenIntentRouter
{
    static string? ExtractPath(string raw)
    {
        const string pathEq = "path=";
        var idx = raw.IndexOf(pathEq, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return raw[(idx + pathEq.Length)..].Trim().Trim('"');
        if (raw.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
            return raw["open ".Length..].Trim().Trim('"');
        return null;
    }

    static string? ExtractLifecyclePath(string raw, string verb)
    {
        if (raw.Equals(verb, StringComparison.OrdinalIgnoreCase))
            return null;
        // Prefer keyed path= (supports quotes / spaces); stop before filter= via ExtractKeyedValue.
        if (ExtractKeyedValue(raw, "path")is { Length: > 0 } keyed)
            return keyed.Trim();
        var prefix = verb + " ";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw[prefix.Length..].Trim();
            if (rest.StartsWith("filter=", StringComparison.OrdinalIgnoreCase))
                return null;
            if (rest.StartsWith('"'))
            {
                var end = rest.IndexOf('"', 1);
                if (end > 0)
                    return rest[1..end];
            }

            var space = rest.IndexOf(' ');
            if (space > 0)
                rest = rest[..space];
            return rest.Length == 0 ? null : rest.Trim().Trim('"');
        }

        return null;
    }

    /// <summary>
    /// <c>replace path=… old="…" new="…"</c> — quoted old/new (spaces ok); path unquoted token.
    /// </summary>
    static bool TryParseReplace(string raw, out string? path, out string? oldString, out string? newString, out string? reason)
    {
        path = null;
        oldString = null;
        newString = null;
        reason = null;
        path = ExtractKeyedValue(raw, "path");
        oldString = ExtractKeyedValue(raw, "old") ?? ExtractKeyedValue(raw, "old_string");
        newString = ExtractKeyedValue(raw, "new") ?? ExtractKeyedValue(raw, "new_string");
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "replace_path_empty";
            return false;
        }

        if (string.IsNullOrEmpty(oldString))
        {
            reason = "replace_old_empty";
            return false;
        }

        newString ??= "";
        return true;
    }

    internal static string? ExtractKeyedValue(string raw, string key)
    {
        var needle = key + "=";
        var idx = raw.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var i = idx + needle.Length;
        if (i >= raw.Length)
            return "";
        if (raw[i] == '"')
        {
            var end = raw.IndexOf('"', i + 1);
            if (end < 0)
                return raw[(i + 1)..];
            return raw[(i + 1)..end];
        }

        var rest = raw[i..];
        var sp = rest.IndexOf(' ');
        return sp < 0 ? rest : rest[..sp];
    }

    static bool TryKv(string raw, out string? detail, out string? scene)
    {
        detail = null;
        scene = null;
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var eq = p.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = p[..eq];
            var val = p[(eq + 1)..];
            if (key.Equals("detail", StringComparison.OrdinalIgnoreCase))
                detail = val;
            else if (key.Equals("scene", StringComparison.OrdinalIgnoreCase))
                scene = val;
        }

        return detail is not null || scene is not null;
    }
}