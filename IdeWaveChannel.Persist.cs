#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeWaveChannel
{
    static object OkScene(WaveDoc doc, string op)
    {
        var done = doc.Items.Count(i => i.Status == "done");
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            op,
            pulse = PulseLine(),
            wave = new
            {
                id = doc.Id,
                title = doc.Title,
                status = doc.Status,
                created_utc = doc.CreatedUtc,
                updated_utc = doc.UpdatedUtc,
                done,
                total = doc.Items.Count,
                items = doc.Items.Select(i => new { id = i.Id, label = i.Label, status = i.Status }).ToArray()
            },
            ops = OpsList,
            next = NextFor(doc),
            hint = doc.Status == "shipped" ? "Wave shipped. Clear or seed next; go=verify_wave for ship checklist." : "Fly the wave — mark items done, then wave shipped. Soft FileLines CLOSED."
        };
    }

    static object[] NextFor(WaveDoc doc) => doc.Status switch
    {
        "shipped" => [new
        {
            go = "verify_wave",
            label = "Verify checklist",
            why = "op=scene"
        }, new
        {
            go = "plan",
            label = "Clear wave",
            why = "cmd=wave clear"
        }

        ],
        "open" => [new
        {
            go = "plan",
            label = "Start shipping",
            why = "cmd=wave start"
        }, new
        {
            go = "inventory",
            label = "Gap inventory",
            why = "op=scene"
        }

        ],
        _ => [new
        {
            go = "plan",
            label = "Mark item done",
            why = "cmd=wave item done <label>"
        }, new
        {
            go = "plan",
            label = "Wave shipped",
            why = "cmd=wave shipped"
        }

        ]
    };
    static object Fail(string error, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        go = GoName,
        error,
        hint
    };
    static readonly string[] OpsList = ["scene", "seed", "start", "item_done", "shipped", "clear", "pulse"];
    static List<string> ParseLabels(IReadOnlyDictionary<string, JsonElement> args)
    {
        var fromItems = Opt(args, "items") ?? Opt(args, "labels") ?? Opt(args, "body") ?? Opt(args, "text") ?? Opt(args, "q") ?? Opt(args, "title_items");
        var titleOnlyFallback = string.IsNullOrWhiteSpace(fromItems);
        var raw = titleOnlyFallback ? Opt(args, "title") : fromItems;
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;
        // title=Foo polish words (no list separators) is a wave name — not a single fake item.
        if (titleOnlyFallback && raw.IndexOfAny([';', ',', '|', '\n', '\r']) < 0)
            return list;
        foreach (var part in SplitLabels(raw))
        {
            var label = part.Trim().TrimStart('-', '*', '•').Trim();
            if (label.Length == 0)
                continue;
            if (list.Any(x => x.Equals(label, StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(label);
        }

        return list;
    }

    static IEnumerable<string> SplitLabels(string raw)
    {
        if (raw.Contains('\n') || raw.Contains('\r'))
            return raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Contains(';'))
            return raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Contains(','))
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Contains('|'))
            return raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return[raw];
    }

    static WaveDoc? Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;
                return JsonSerializer.Deserialize<WaveDoc>(File.ReadAllText(FilePath), JsonOpts);
            }
            catch
            {
                return null;
            }
        }
    }

    static void Save(WaveDoc doc)
    {
        lock (Gate)
        {
            var path = FilePath;
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts), Encoding.UTF8);
            File.Move(tmp, path, overwrite: true);
        }
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Array => string.Join(';', el.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s))),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()};
    }

    static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    internal sealed class WaveDoc
    {
        public string Schema { get; set; } = SchemaVersion;
        public string Id { get; set; } = "";
        public string Title { get; set; } = "wave";
        public string Status { get; set; } = "open";
        public string? CreatedUtc { get; set; }
        public string? UpdatedUtc { get; set; }
        public List<WaveItem> Items { get; set; } = [];
    }

    internal sealed class WaveItem
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Status { get; set; } = "pending";
    }
}