#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    static void ApplyGoArgsOnly(Dictionary<string, JsonElement> merged, IReadOnlyList<string> tokens, int start)
    {
        if (tokens.Count <= start)
            return;

        var ga = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (merged.TryGetValue("go_args", out var existing) && existing.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in existing.EnumerateObject())
                ga[p.Name] = p.Value.Clone();
        }

        for (var i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var eq = t.IndexOf('=');
            if (eq > 0)
            {
                ga[t[..eq]] = JsonSerializer.SerializeToElement(t[(eq + 1)..]);
                continue;
            }

            if (t.Contains("://", StringComparison.Ordinal) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                ga["url"] = JsonSerializer.SerializeToElement(t);
            else if (t.Contains('\\') || t.Contains('/') || t.Contains('.'))
                ga["path"] = JsonSerializer.SerializeToElement(t);
            else if (!ga.ContainsKey("q"))
                ga["q"] = JsonSerializer.SerializeToElement(t);
            else
                ga[$"arg{i}"] = JsonSerializer.SerializeToElement(t);
        }

        merged["go_args"] = JsonSerializer.SerializeToElement(ga);
    }

    static void ApplyGo(Dictionary<string, JsonElement> merged, IReadOnlyList<string> tokens, int start)
    {
        merged["go"] = JsonSerializer.SerializeToElement(tokens[start]);
        if (tokens.Count <= start + 1)
            return;

        ApplyGoArgsOnly(merged, tokens, start: start + 1);
    }

    static List<string> Tokenize(string line)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        char quote = '\0';
        foreach (var ch in line)
        {
            if (inQuote)
            {
                if (ch == quote) { inQuote = false; continue; }
                sb.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuote = true;
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }

                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
            list.Add(sb.ToString());
        return list;
    }

    static object Help(string? note) => new
    {
        ok = true,
        schema = SchemaVersion,
        role = "ccl_help",
        note,
        alias = "ccc",
        examples =
            new[]
            {
                "layout agent",
                "probe",
                "check",
                "run",
                "report",
                "alert",
                "sa",
                "problems",
                "problems 1",
                "plugins",
                "plugins search plantuml",
                "plugins want plantuml",
                "plugins install jebbs.plantuml",
                "plugins groups",
                "plugins disable group diagrams",
                "plugins group add jebbs.plantuml work",
                "plugins preview",
                "sys",
                "chk",
                "ecl",
                "qrh",
                "qrh open dap-pdb-lock",
                "qrh search pdb",
                "eqrh",
                "review",
                "review files",
                "nav",
                "gates",
                "go report",
                "go alert",
                "full report",
                "feature desk-comfort",
                "task ship-omit @act",
                "phase act",
                "promote",
                "share",
                "share with operator",
                "share plan",
                "share report",
                "deploy",
                "deploy dry",
                "confirm",
                "reject",
                "plan",
                "go browser",
                "seat m git",
                "clear",
            },
        hint = "CCL (cmd=). Channels: sit/plan · work/editor · probe/script · report · alert · sys/ecl/qrh/review. CCC=help."
    };


    /// <summary>
    /// <c>plugins disable group javascript</c> | <c>plugins enable g1</c> | <c>plugins disable jebbs.plantuml</c>
    /// </summary>
    static object ParsePluginsEnableDisable(IReadOnlyList<string> tokens, string sub)
    {
        var op = sub is "on" or "enable" ? "enable" : "disable";
        if (tokens.Count < 3)
            return new { op };

        if (tokens[2].Equals("group", StringComparison.OrdinalIgnoreCase)
            || tokens[2].Equals("grp", StringComparison.OrdinalIgnoreCase))
        {
            var group = tokens.Count >= 4 ? tokens[3] : "";
            return new { op, group };
        }

        var target = tokens[2];
        if (target.StartsWith('g') && int.TryParse(target.AsSpan(1), out _))
            return new { op, row = target };
        return new { op, id = target };
    }

    /// <summary><c>plugins group add jebbs.plantuml work</c> | <c>plugins group remove …</c></summary>
    static object ParsePluginsGroup(IReadOnlyList<string> tokens)
    {
        // plugins group add|remove <id> <group>
        if (tokens.Count < 5)
            return new { op = "group" };
        var sub = tokens[2].ToLowerInvariant();
        if (sub is not ("add" or "remove" or "rm" or "del"))
            return new { op = "group", id = tokens[2], group = tokens[3], sub = "add" };
        return new { op = "group", sub, id = tokens[3], group = tokens[4] };
    }

    /// <summary>
    /// <c>plugins install jebbs.plantuml</c> | <c>… id version</c> | <c>… path.vsix</c> | <c>… s1</c>
    /// </summary>
    static object ParsePluginsInstall(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 3)
            return new { op = "install" };

        var target = tokens[2];
        var version = tokens.Count >= 4 ? tokens[3] : null;

        // row from last search
        if (target.StartsWith('s') && int.TryParse(target.AsSpan(1), out _))
            return version is { Length: > 0 }
                ? new { op = "install", row = target, version }
                : new { op = "install", row = target };

        // local path / vsix
        if (LooksLikeLocalPluginPath(target))
            return new { op = "install", path = target };

        // Open VSX id
        return version is { Length: > 0 }
            ? new { op = "install", id = target, version }
            : new { op = "install", id = target };
    }

    static bool LooksLikeLocalPluginPath(string target)
    {
        if (target.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase))
            return true;
        if (target.Contains('/') || target.Contains('\\') || target.Contains(':'))
            return true;
        try
        {
            if (File.Exists(target) || Directory.Exists(target))
                return true;
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    static object Err(string error, string hint) => new { ok = false, schema = SchemaVersion, role = "ccl", error, hint };
}
