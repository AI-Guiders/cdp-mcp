#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeChkChannel
{
    static string AckKey(string checklistId, string itemId) => $"{checklistId.Trim()}:{itemId.Trim()}".ToLowerInvariant();
    static List<string> ParseLinks(Dictionary<string, JsonElement> args)
    {
        var list = new List<string>();
        void AddOne(string? raw)
        {
            if (raw is not { Length: > 0 })
                return;
            foreach (var part in raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var n = NormalizeLink(part);
                if (n.Length > 0 && !list.Contains(n, StringComparer.OrdinalIgnoreCase))
                    list.Add(n);
            }
        }

        AddOne(Opt(args, "link"));
        AddOne(Opt(args, "links"));
        AddOne(Opt(args, "on"));
        return list;
    }

    static string NormalizeLink(string link)
    {
        var s = link.Trim();
        // Allow bare "handoff" → phase:handoff when known phase/intent/state tokens
        if (!s.Contains(':', StringComparison.Ordinal))
        {
            var low = s.ToLowerInvariant();
            if (low is "explore" or "clarify" or "recall" or "plan" or "act" or "verify" or "review" or "handoff")
                return "phase:" + low;
            if (low is "ship" or "fix" or "deploy")
                return "intent:" + low;
            if (low.StartsWith("git.", StringComparison.Ordinal) || low.StartsWith("dap.", StringComparison.Ordinal) || low is "always")
                return low == "always" ? "always" : "state:" + low;
        }

        return s;
    }

    static string SanitizeId(string id)
    {
        var chars = id.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string (chars).Trim('-');
    }

    static ChecklistDef ToDef(OverlayChecklist c, bool builtin) => new(c.Id, c.Title ?? c.Id, c.Links ?? [], (c.MemoryItems ?? []).Select(ToItem).ToArray(), (c.Items ?? []).Select(ToItem).ToArray(), builtin, c.Enabled);
    static ItemDef ToItem(OverlayItem i) => new(i.Id, i.Kind ?? "do", i.Text ?? i.Id, i.Probe, i.Action, i.Required);
    static OverlayDoc LoadOverlay()
    {
        var raw = IdeSettingsStore.GetOrNull(OverlayKey) ?? IdeSettingsStore.GetOrNull(LegacyOverlayKey);
        if (raw is not { Length: > 0 })
            return new OverlayDoc();
        try
        {
            return JsonSerializer.Deserialize<OverlayDoc>(raw, JsonOpts) ?? new OverlayDoc();
        }
        catch
        {
            return new OverlayDoc();
        }
    }

    static void SaveOverlay(OverlayDoc doc)
    {
        IdeSettingsStore.Set(OverlayKey, JsonSerializer.Serialize(doc, JsonOpts));
        IdeSettingsStore.Unset(LegacyOverlayKey);
    }

    static HashSet<string> LoadAcks()
    {
        var raw = IdeSettingsStore.GetOrNull(AcksKey) ?? IdeSettingsStore.GetOrNull(LegacyAcksKey);
        if (raw is not { Length: > 0 })
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(raw, JsonOpts) ?? [];
            return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    static void SaveAcks(HashSet<string> acks)
    {
        IdeSettingsStore.Set(AcksKey, JsonSerializer.Serialize(acks.OrderBy(x => x).ToList(), JsonOpts));
        IdeSettingsStore.Unset(LegacyAcksKey);
    }

    static object Err(string error, string hint) => new
    {
        ok = false,
        error,
        hint
    };
    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null)
            return d;
        foreach (var kv in args)
            d[kv.Key] = kv.Value;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
                d[p.Name] = p.Value.Clone();
        }

        return d;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static bool Flag(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b,
            _ => false
        };
    }
}