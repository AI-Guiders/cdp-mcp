#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// TM Wave — durable active wave (seat JSON). REPL: <c>wave seed|scene|start|item done|shipped|clear</c>.
/// Affordance for list→batch→ship (not single-leaf mill). Not SoftOrgan; lives beside plan board.
/// </summary>
internal static class IdeWaveChannel
{
    public const string SchemaVersion = "wave_channel/v1";
    public const string GoName = "plan";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly object Gate = new();

    /// <summary>Tests: redirect store path.</summary>
    internal static Func<string>? FilePathOverride { get; set; }

    public static string FilePath =>
        FilePathOverride?.Invoke()
        ?? Path.Combine(CdpProfile.StateRoot, IdeIgniteArmHost.Seat, "active-wave.json");

    public static string HandleJson(IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(args), new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "tm_op") ?? Opt(args, "cmd") ?? "scene")
            .Trim().ToLowerInvariant();
        return op switch
        {
            "seed" or "new" or "create" => Seed(args),
            "start" or "shipping" => Start(),
            "done" or "item_done" or "item" => ItemDone(args),
            "shipped" or "complete" or "close" => Shipped(),
            "clear" or "drop" or "rm" => Clear(),
            "pulse" or "a" => Pulse(),
            _ => Scene()
        };
    }

    public static string PulseLine()
    {
        var doc = Load();
        if (doc is null || doc.Status is "shipped" or "cleared")
            return "wave · idle";
        var done = doc.Items.Count(i => i.Status == "done");
        return $"wave · {doc.Status} · {done}/{doc.Items.Count} · {Trim(doc.Title, 28)}";
    }

    public static bool HasActiveOpen()
    {
        var doc = Load();
        return doc is { Status: "open" or "shipping" } && doc.Items.Count > 0;
    }

    public static WaveDoc? TryLoadActive()
    {
        var doc = Load();
        return doc is { Status: "open" or "shipping" } ? doc : null;
    }

    static object Scene()
    {
        var doc = Load();
        if (doc is null)
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                go = GoName,
                op = "scene",
                pulse = "wave · idle",
                empty = true,
                ops = OpsList,
                next = new object[]
                {
                    new { go = "plan", label = "Seed wave", why = "cmd=wave seed a;b;c" },
                    new { go = "inventory", label = "Inventory gaps", why = "op=scene" }
                },
                hint = "No active wave. Seed: cmd=\"wave seed label1;label2;label3\" (or newline / - yaml). Soft FileLines CLOSED — list→batch→ship."
            };
        }

        return OkScene(doc, "scene");
    }

    static object Pulse()
    {
        var doc = Load();
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            op = "pulse",
            pulse = PulseLine(),
            empty = doc is null
        };
    }

    static object Seed(IReadOnlyDictionary<string, JsonElement> args)
    {
        var title = (Opt(args, "title") ?? Opt(args, "name") ?? "wave").Trim();
        if (title.Length == 0) title = "wave";
        var labels = ParseLabels(args);
        if (labels.Count == 0)
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                go = GoName,
                op = "seed",
                error = "items_required",
                hint = "wave seed a;b;c | wave seed items=a,b,c | body with newlines / - yaml"
            };
        }

        var now = DateTime.UtcNow.ToString("o");
        var doc = new WaveDoc
        {
            Schema = SchemaVersion,
            Id = "w-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
            Title = title,
            Status = "open",
            CreatedUtc = now,
            UpdatedUtc = now,
            Items = labels.Select((label, i) => new WaveItem
            {
                Id = $"i{i + 1}",
                Label = label,
                Status = "pending"
            }).ToList()
        };
        Save(doc);
        return OkScene(doc, "seed");
    }

    static object Start()
    {
        var doc = Load();
        if (doc is null)
            return Fail("no_wave", "wave seed … first");
        doc.Status = "shipping";
        doc.UpdatedUtc = DateTime.UtcNow.ToString("o");
        Save(doc);
        return OkScene(doc, "start");
    }

    static object ItemDone(IReadOnlyDictionary<string, JsonElement> args)
    {
        var doc = Load();
        if (doc is null)
            return Fail("no_wave", "wave seed … first");

        var label = (Opt(args, "label") ?? Opt(args, "item") ?? Opt(args, "title") ?? Opt(args, "q") ?? "")
            .Trim();
        if (label.Length == 0)
            return Fail("label_required", "wave item done <label>");

        var hit = doc.Items.FirstOrDefault(i =>
            i.Label.Equals(label, StringComparison.OrdinalIgnoreCase)
            || i.Id.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            return Fail("item_not_found", $"no item '{label}' — wave scene");

        hit.Status = "done";
        if (doc.Status == "open")
            doc.Status = "shipping";
        doc.UpdatedUtc = DateTime.UtcNow.ToString("o");
        if (doc.Items.All(i => i.Status == "done"))
            doc.Status = "shipped";
        Save(doc);
        return OkScene(doc, "item_done");
    }

    static object Shipped()
    {
        var doc = Load();
        if (doc is null)
            return Fail("no_wave", "wave seed … first");
        foreach (var i in doc.Items)
            i.Status = "done";
        doc.Status = "shipped";
        doc.UpdatedUtc = DateTime.UtcNow.ToString("o");
        Save(doc);
        return OkScene(doc, "shipped");
    }

    static object Clear()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                /* best-effort */
            }
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            op = "clear",
            pulse = "wave · idle",
            empty = true,
            hint = "Wave cleared. Next: cmd=wave seed … or go=inventory."
        };
    }

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
            hint = doc.Status == "shipped"
                ? "Wave shipped. Clear or seed next; go=verify_wave for ship checklist."
                : "Fly the wave — mark items done, then wave shipped. Soft FileLines CLOSED."
        };
    }

    static object[] NextFor(WaveDoc doc) =>
        doc.Status switch
        {
            "shipped" =>
            [
                new { go = "verify_wave", label = "Verify checklist", why = "op=scene" },
                new { go = "plan", label = "Clear wave", why = "cmd=wave clear" }
            ],
            "open" =>
            [
                new { go = "plan", label = "Start shipping", why = "cmd=wave start" },
                new { go = "inventory", label = "Gap inventory", why = "op=scene" }
            ],
            _ =>
            [
                new { go = "plan", label = "Mark item done", why = "cmd=wave item done <label>" },
                new { go = "plan", label = "Wave shipped", why = "cmd=wave shipped" }
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
        var fromItems = Opt(args, "items")
            ?? Opt(args, "labels")
            ?? Opt(args, "body")
            ?? Opt(args, "text")
            ?? Opt(args, "q")
            ?? Opt(args, "title_items");
        var titleOnlyFallback = string.IsNullOrWhiteSpace(fromItems);
        var raw = titleOnlyFallback ? Opt(args, "title") : fromItems;

        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        // title=Foo polish words (no list separators) is a wave name — not a single fake item.
        if (titleOnlyFallback
            && raw.IndexOfAny([';', ',', '|', '\n', '\r']) < 0)
            return list;

        foreach (var part in SplitLabels(raw))
        {
            var label = part.Trim().TrimStart('-', '*', '•').Trim();
            if (label.Length == 0) continue;
            if (list.Any(x => x.Equals(label, StringComparison.OrdinalIgnoreCase))) continue;
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
        return [raw];
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
            JsonValueKind.Array => string.Join(';', el.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()
        };
    }

    static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

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
