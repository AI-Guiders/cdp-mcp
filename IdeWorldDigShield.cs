#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// World-dig shield — SoftFL invent / invent theater / board-hygiene cannot Done without dig evidence.
/// Parity with IdeHumanFaceShield: refuse until dig= (path|pack|URL|kb) or force=.
/// Research freedom: dig IS the work under doubt; slap-slap invent mill = seeming.
/// </summary>
internal static class IdeWorldDigShield
{
    internal const string RefuseId = "world_dig_missing";

    static readonly string[] InventMillMarkers =
    [
        "softfl",
        "soft filelines",
        "invent theater",
        "invent only",
        "board-hygiene",
        "board hygiene",
        "dig reject",
        "meta reopen",
        "tm-cleanup",
        "tm cleanup",
        "inventory mill"
    ];

    internal static void RefuseInventMillDoneWithoutDig(
        IntentWorkspaceStore store,
        Guid stageId,
        IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (ForceArg(args))
            return;
        if (HasDigEvidence(args))
            return;

        var peek = store.TryGetStageTitleProduct(stageId);
        if (peek is null)
            return;
        var (title, _) = peek.Value;
        if (!LooksLikeInventMill(title))
            return;

        throw new ArgumentException(
            $"task_done refused — {RefuseId}: invent-mill / SoftFL / board-hygiene leaf needs dig= " +
            "(path|pack|URL|kb) — research before Done. force=true escape.");
    }

    internal static bool LooksLikeInventMill(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        var t = title.Trim();
        foreach (var m in InventMillMarkers)
        {
            if (t.Contains(m, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool HasDigEvidence(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;

        foreach (var key in new[] { "dig", "kb", "pack", "source", "source_url", "browser", "dig_path" })
        {
            if (TryNonEmpty(args, key))
                return true;
        }

        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
            {
                if ((p.NameEquals("dig") || p.NameEquals("kb") || p.NameEquals("pack")
                     || p.NameEquals("source") || p.NameEquals("source_url") || p.NameEquals("browser")
                     || p.NameEquals("dig_path"))
                    && IsNonEmpty(p.Value))
                    return true;
            }
        }

        return false;
    }

    static bool TryNonEmpty(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return IsNonEmpty(el);
    }

    static bool IsNonEmpty(JsonElement el) =>
        el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString());

    static bool Boolish(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.True)
            return true;
        return el.ValueKind == JsonValueKind.String
               && bool.TryParse(el.GetString(), out var b)
               && b;
    }

    static bool ForceArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;
        if (Boolish(args, "force"))
            return true;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty("force", out var f))
        {
            if (f.ValueKind == JsonValueKind.True)
                return true;
            if (f.ValueKind == JsonValueKind.String && bool.TryParse(f.GetString(), out var b) && b)
                return true;
        }

        return false;
    }
}
