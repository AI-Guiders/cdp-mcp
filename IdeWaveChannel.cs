#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// TM Wave — durable active wave (seat JSON). REPL: <c>wave seed|scene|start|item done|shipped|clear</c>.
/// Affordance for list→batch→ship (not single-leaf mill). Not SoftInstrument; lives beside plan board.
/// </summary>
internal static partial class IdeWaveChannel
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
            "shipped" or "complete" or "close" => Shipped(args),
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
        {
            if (IdeSeemingDoneShield.IsHumanFacedText(IdeSeemingDoneShield.WaveBlob(doc)))
            {
                // Human-faced rectangle closes only on explicit wave shipped + teeth — not last item_done.
                if (doc.Status == "open")
                    doc.Status = "shipping";
            }
            else if (IdeWaveShipShield.TryRefuse(doc, args, out var err, out var hint))
                return Fail(err, hint);
            else
                doc.Status = "shipped";
        }
        Save(doc);
        return OkScene(doc, "item_done");
    }

    static object Shipped(IReadOnlyDictionary<string, JsonElement> args)
    {
        var doc = Load();
        if (doc is null)
            return Fail("no_wave", "wave seed … first");
        if (IdeWaveShipShield.TryRefuse(doc, args, out var err, out var hint))
            return Fail(err, hint);
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

}
