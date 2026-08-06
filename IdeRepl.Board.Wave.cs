#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>TM Wave verbs — Direct to <see cref="IdeWaveChannel"/> (avoids multi_cmd serial theatre).</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryBoardWave(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
        if (head is not "wave")
            return null;

        if (tokens.Count < 2)
        {
            merged["go"] = JsonSerializer.SerializeToElement("plan");
            return (merged, IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("scene")
            }));
        }

        var sub = tokens[1].ToLowerInvariant();
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (sub is "scene" or "status" or "pulse" or "a")
        {
            args["op"] = JsonSerializer.SerializeToElement(sub is "a" ? "pulse" : sub is "status" ? "scene" : sub);
        }
        else if (sub is "seed" or "new" or "create")
        {
            args["op"] = JsonSerializer.SerializeToElement("seed");
            var rest = tokens.Skip(2).ToList();
            ParseWaveSeedRest(rest, args);
        }
        else if (sub is "start" or "shipping")
        {
            args["op"] = JsonSerializer.SerializeToElement("start");
        }
        else if (sub is "shipped" or "complete" or "close")
        {
            args["op"] = JsonSerializer.SerializeToElement("shipped");
            MergeWaveShipArgs(tokens, skip: 2, merged, args);
        }
        else if (sub is "clear" or "drop" or "rm")
        {
            args["op"] = JsonSerializer.SerializeToElement("clear");
        }
        else if (sub is "item" or "done")
        {
            // wave item done <label> | wave done <label>
            args["op"] = JsonSerializer.SerializeToElement("item_done");
            var skip = sub == "item" && tokens.Count >= 3
                && tokens[2].Equals("done", StringComparison.OrdinalIgnoreCase)
                ? 3
                : 2;
            var labelParts = new List<string>();
            for (var i = skip; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.IndexOf('=') > 0)
                    break;
                labelParts.Add(t);
            }

            var label = string.Join(' ', labelParts).Trim();
            if (label.Length == 0)
                return (merged, Err("wave item done needs label", "wave item done <label>"));
            args["label"] = JsonSerializer.SerializeToElement(label);
            MergeWaveShipArgs(tokens, skip, merged, args);
        }
        else
        {
            // wave a;b;c → seed
            args["op"] = JsonSerializer.SerializeToElement("seed");
            var rest = tokens.Skip(1).ToList();
            ParseWaveSeedRest(rest, args);
        }

        merged["go"] = JsonSerializer.SerializeToElement("plan");
        return (merged, IdeWaveChannel.Handle(args));
    }

        static void ParseWaveSeedRest(IReadOnlyList<string> rest, Dictionary<string, JsonElement> args)
    {
        if (rest.Count == 0)
            return;

        string? title = null;
        var itemParts = new List<string>();
        var hadItemsKey = false;
        var collecting = "none"; // none | title | items
        foreach (var tok in rest)
        {
            if (tok.StartsWith("title=", StringComparison.OrdinalIgnoreCase)
                || tok.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
            {
                collecting = "title";
                var keyLen = tok.StartsWith("title=", StringComparison.OrdinalIgnoreCase) ? "title=".Length : "name=".Length;
                title = tok[keyLen..].Trim().Trim('"');
                continue;
            }

            if (tok.StartsWith("items=", StringComparison.OrdinalIgnoreCase))
            {
                hadItemsKey = true;
                collecting = "items";
                var chunk = tok["items=".Length..].Trim().Trim('"');
                if (chunk.Length > 0)
                    itemParts.Add(chunk);
                continue;
            }

            if (collecting == "title")
            {
                title = ((title ?? "") + " " + tok).Trim();
                continue;
            }

            if (collecting == "items")
            {
                // Space-split after items= must stay one blob until ;|, — not one fake item per word.
                if (itemParts.Count == 0)
                    itemParts.Add(tok);
                else
                    itemParts[^1] = itemParts[^1] + " " + tok;
                continue;
            }

            itemParts.Add(tok);
        }

        // title=Foo polish words without separators → extend title (Autoi footgun),
        // not invent 14 fake wave items. Prefer items=a;b;c when title= is set.
        if (title is { Length: > 0 }
            && !hadItemsKey
            && itemParts.Count > 0
            && itemParts.All(p => p.IndexOfAny([';', ',', '|', '\n', '\r']) < 0))
        {
            title = (title + " " + string.Join(' ', itemParts)).Trim();
            itemParts.Clear();
        }

        var joined = string.Join(';', itemParts);
        if (joined.Length > 0)
            args["items"] = JsonSerializer.SerializeToElement(joined);
        if (title is { Length: > 0 })
            args["title"] = JsonSerializer.SerializeToElement(title);
    }

    /// <summary>Preserve cockpit evidence=/domain=/force= for wave shipped human-face teeth.
    /// Lived 2026-08-06: unquoted evidence path with spaces (Personal Cursor Folder) splits
    /// into broken path then shield refuse. Join pathish values until next ship-key=.</summary>
    static void MergeWaveShipArgs(
        IReadOnlyList<string> tokens,
        int skip,
        Dictionary<string, JsonElement> merged,
        Dictionary<string, JsonElement> args)
    {
        if (merged.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
            {
                if (p.Name is "evidence" or "shot_path" or "png" or "screenshot_path"
                    or "domain" or "stamp" or "domain_id" or "force" or "project_root" or "workspace_path")
                    args[p.Name] = p.Value.Clone();
            }
        }

        foreach (var key in new[] { "evidence", "domain", "force", "project_root" })
        {
            if (merged.TryGetValue(key, out var top) && !args.ContainsKey(key))
                args[key] = top.Clone();
        }

        for (var i = skip; i < tokens.Count;)
        {
            var t = tokens[i];
            var eq = t.IndexOf('=');
            if (eq <= 0)
            {
                i++;
                continue;
            }

            var key = t[..eq];
            var value = t[(eq + 1)..].Trim().Trim('"').Trim('\'');
            i++;
            if (IsPathishWaveShipKey(key))
            {
                while (i < tokens.Count && !IsWaveShipKeyToken(tokens[i]))
                {
                    value = (value + " " + tokens[i]).Trim();
                    i++;
                }

                value = value.Trim().Trim('"').Trim('\'');
            }

            args[key] = JsonSerializer.SerializeToElement(value);
        }
    }

    static bool IsPathishWaveShipKey(string key) =>
        key is "evidence" or "shot_path" or "png" or "screenshot_path"
            or "project_root" or "workspace_path";

    static bool IsWaveShipKeyToken(string tok)
    {
        var eq = tok.IndexOf('=');
        if (eq <= 0)
            return false;
        var key = tok[..eq];
        return key is "evidence" or "shot_path" or "png" or "screenshot_path"
            or "domain" or "stamp" or "domain_id" or "force"
            or "project_root" or "workspace_path";
    }
}
