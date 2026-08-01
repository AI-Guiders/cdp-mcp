#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>CCL verbs peeled from Apply (soft-warn). null = not handled.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryOrgans(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
        if (head is "plugins" or "plugin" or "vsix")
        {
            merged["go"] = JsonSerializer.SerializeToElement("plugins");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "search" or "find" or "query")
                {
                    var q = tokens.Count >= 3 ? string.Join(' ', tokens.Skip(2)) : "";
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "search", q });
                }
                else if (sub is "install" or "add")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(ParsePluginsInstall(tokens));
                }
                else if (sub is "want" or "need" or "get")
                {
                    var q = tokens.Count >= 3 ? string.Join(' ', tokens.Skip(2)) : "";
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "want", q });
                }
                else if (sub is "preview" or "render" or "png")
                {
                    var path = tokens.Count >= 3 ? tokens[2] : null;
                    merged["go_args"] = path is { Length: > 0 }
                        ? JsonSerializer.SerializeToElement(new { op = "preview", path })
                        : JsonSerializer.SerializeToElement(new { op = "preview" });
                }
                else if (sub is "list" or "installed")
                {
                    var all = tokens.Count >= 3 && tokens[2].Equals("all", StringComparison.OrdinalIgnoreCase);
                    merged["go_args"] = all
                        ? JsonSerializer.SerializeToElement(new { op = "list", all = true })
                        : JsonSerializer.SerializeToElement(new { op = "list" });
                }
                else if (sub is "reharvest" or "rescan" or "reclassify")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "reharvest" });
                }
                else if (sub is "groups" or "grouplist")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "groups" });
                }
                else if (sub is "enable" or "on" or "disable" or "off")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(ParsePluginsEnableDisable(tokens, sub));
                }
                else if (sub is "group" or "tag")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(ParsePluginsGroup(tokens));
                }
                else
                {
                    // "plugins s1" → install from last search; "plugins plantuml" → search
                    if (tokens[1].StartsWith('s') && int.TryParse(tokens[1].AsSpan(1), out _))
                    {
                        merged["go_args"] = JsonSerializer.SerializeToElement(
                            new { op = "install", row = tokens[1] });
                    }
                    else if (tokens[1].StartsWith('g') && int.TryParse(tokens[1].AsSpan(1), out _)
                             || int.TryParse(tokens[1], out _))
                    {
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { row = tokens[1] });
                    }
                    else
                    {
                        var q = string.Join(' ', tokens.Skip(1));
                        merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "search", q });
                    }
                }
            }
            return (merged, null);
        }

        if (head is "sys")
        {
            merged["go"] = JsonSerializer.SerializeToElement("sys");
            return (merged, null);
        }

        if (head is "chk" or "ecl")
        {
            merged["go"] = JsonSerializer.SerializeToElement("ecl");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "list" or "catalog")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "list" });
                }
                else if (sub is "reset")
                {
                    var what = tokens.Count >= 3 ? tokens[2] : "overlay";
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "reset", what });
                }
                else if (sub is "ack" or "done" or "unack")
                {
                    if (tokens.Count < 4)
                        return (merged, Err("ecl ack needs checklist+item", "ecl ack ship push"));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new
                    {
                        op = sub == "done" ? "ack" : sub,
                        checklist = tokens[2],
                        item = tokens[3]
                    });
                }
                else if (sub is "add")
                {
                    // ecl add id=foo title=Bar link=phase:act  OR  ecl add foo phase:act
                    var id = tokens.Count >= 3 ? tokens[2] : null;
                    string? title = null;
                    string? link = null;
                    for (var i = 2; i < tokens.Count; i++)
                    {
                        var t = tokens[i];
                        if (t.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
                            id = t[3..];
                        else if (t.StartsWith("title=", StringComparison.OrdinalIgnoreCase))
                            title = t[6..];
                        else if (t.StartsWith("link=", StringComparison.OrdinalIgnoreCase)
                                 || t.StartsWith("links=", StringComparison.OrdinalIgnoreCase))
                            link = t[(t.IndexOf('=') + 1)..];
                        else if (i == 2 && id is null)
                            id = t;
                        else if (link is null && t.Contains(':', StringComparison.Ordinal))
                            link = t;
                        else if (title is null && i > 2)
                            title = t;
                    }

                    if (id is null || link is null)
                        return (merged, Err("ecl add needs id+link", "ecl add mine phase:act"));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "add", id, title, link });
                }
                else if (sub is "remove" or "rm" or "enable" or "disable" or "on" or "off")
                {
                    if (tokens.Count < 3)
                        return (merged, Err("ecl needs id", $"ecl {sub} ship"));
                    var op = sub is "on" ? "enable" : sub is "off" ? "disable" : sub is "rm" ? "remove" : sub;
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op, id = tokens[2] });
                }
                else if (sub is "link" or "unlink")
                {
                    if (tokens.Count < 4)
                        return (merged, Err("ecl link needs id+link", "ecl link ship phase:verify"));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new
                    {
                        op = sub,
                        id = tokens[2],
                        link = tokens[3]
                    });
                }
                else
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "run" });
                }
            }

            return (merged, null);
        }

        if (head is "qrh" or "eqrh" or "handbook")
        {
            merged["go"] = JsonSerializer.SerializeToElement("qrh");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "index" or "list" or "catalog")
                {
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "index" });
                }
                else if (sub is "search" or "find")
                {
                    if (tokens.Count < 3)
                        return (merged, Err("qrh search needs q", "qrh search pdb"));
                    var q = string.Join(' ', tokens.Skip(2));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "search", q });
                }
                else if (sub is "shelf" or "section")
                {
                    if (tokens.Count < 3)
                        return (merged, Err("qrh shelf needs name", "qrh shelf emergency"));
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "shelf", shelf = tokens[2] });
                }
                else if (sub is "related")
                {
                    var id = tokens.Count >= 3 ? tokens[2] : null;
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "related", id });
                }
                else if (sub is "open")
                {
                    var id = tokens.Count >= 3 ? tokens[2] : null;
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "open", id });
                }
                else
                {
                    // Bare page id: qrh dap-pdb-lock
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "open", id = tokens[1] });
                }
            }

            return (merged, null);
        }

        if (head is "review")
        {
            merged["go"] = JsonSerializer.SerializeToElement("review");
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "files" or "list" or "index")
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "files" });
                else if (sub is "open")
                {
                    var path = tokens.Count >= 3 ? string.Join(' ', tokens.Skip(2)) : null;
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "open", path });
                }
                else
                    merged["go_args"] = JsonSerializer.SerializeToElement(new { op = "open", path = tokens[1] });
            }

            return (merged, null);
        }

        if (head is "nav")
        {
            merged["go"] = JsonSerializer.SerializeToElement("nav");
            return (merged, null);
        }

        if (head is "gates" or "quality")
        {
            merged["go"] = JsonSerializer.SerializeToElement("quality");
            return (merged, null);
        }

        return null;
    }
}
