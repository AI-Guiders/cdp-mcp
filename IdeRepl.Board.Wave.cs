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
            var label = string.Join(' ', tokens.Skip(skip)).Trim();
            if (label.Length == 0)
                return (merged, Err("wave item done needs label", "wave item done <label>"));
            args["label"] = JsonSerializer.SerializeToElement(label);
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
        foreach (var tok in rest)
        {
            if (tok.StartsWith("title=", StringComparison.OrdinalIgnoreCase))
            {
                title = tok["title=".Length..].Trim().Trim('"');
                continue;
            }

            if (tok.StartsWith("items=", StringComparison.OrdinalIgnoreCase))
            {
                itemParts.Add(tok["items=".Length..].Trim().Trim('"'));
                continue;
            }

            if (tok.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
            {
                title = tok["name=".Length..].Trim().Trim('"');
                continue;
            }

            itemParts.Add(tok);
        }

        var joined = string.Join(';', itemParts);
        if (joined.Length > 0)
            args["items"] = JsonSerializer.SerializeToElement(joined);
        if (title is { Length: > 0 })
            args["title"] = JsonSerializer.SerializeToElement(title);
        else if (itemParts.Count > 1
                 && !joined.Contains(';')
                 && !joined.Contains(',')
                 && itemParts[0].IndexOfAny([';', ',', '|', '\n']) < 0)
        {
            // wave seed Throughput a b c — first token title, rest items
            // only when no separators in first token and multiple tokens without items=
            // Skip: if first looks like items list already joined
        }
    }
}
