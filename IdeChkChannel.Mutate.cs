#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeChkChannel
{
    static object DoEnable(Dictionary<string, JsonElement> args, bool enable)
    {
        var id = Opt(args, "id") ?? Opt(args, "name");
        if (id is not { Length: > 0 })
            return Err("id_required", "ecl enable id=ship");

        var overlay = LoadOverlay();
        if (enable)
        {
            overlay.Disabled?.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            overlay.Enabled ??= [];
            if (!overlay.Enabled.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                overlay.Enabled.Add(id);
            if (overlay.Custom is { } customs)
            {
                var ix = customs.FindIndex(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (ix >= 0)
                    customs[ix].Enabled = true;
            }
        }
        else
        {
            overlay.Enabled?.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            overlay.Disabled ??= [];
            if (!overlay.Disabled.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                overlay.Disabled.Add(id);
            if (overlay.Custom is { } customs)
            {
                var ix = customs.FindIndex(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (ix >= 0)
                    customs[ix].Enabled = false;
            }
        }

        SaveOverlay(overlay);
        return new { ok = true, op = enable ? "enable" : "disable", id };
    }

    static object DoAck(Dictionary<string, JsonElement> args, bool unack = false)
    {
        var checklist = Opt(args, "checklist") ?? Opt(args, "id") ?? Opt(args, "name");
        var item = Opt(args, "item") ?? Opt(args, "step");
        // Allow "chk ack ship push" style via positional: checklist + item already in id/item
        if (item is null && Opt(args, "arg1") is { } a1 && Opt(args, "arg0") is { } a0)
        {
            checklist = a0;
            item = a1;
        }

        if (checklist is not { Length: > 0 } || item is not { Length: > 0 })
            return Err("checklist_item_required", "ecl ack ship push");

        var acks = LoadAcks();
        var key = AckKey(checklist, item);
        if (unack)
            acks.Remove(key);
        else
            acks.Add(key);
        SaveAcks(acks);
        return new { ok = true, op = unack ? "unack" : "ack", checklist, item, key };
    }

    static object DoReset(Dictionary<string, JsonElement> args)
    {
        var what = (Opt(args, "what") ?? Opt(args, "scope") ?? "overlay").Trim().ToLowerInvariant();
        if (what is "acks" or "ack")
        {
            IdeSettingsStore.Unset(AcksKey);
            IdeSettingsStore.Unset(LegacyAcksKey);
            return new { ok = true, op = "reset", what = "acks" };
        }

        if (what is "all")
        {
            IdeSettingsStore.Unset(OverlayKey);
            IdeSettingsStore.Unset(LegacyOverlayKey);
            IdeSettingsStore.Unset(AcksKey);
            IdeSettingsStore.Unset(LegacyAcksKey);
            return new { ok = true, op = "reset", what = "all" };
        }

        IdeSettingsStore.Unset(OverlayKey);
        IdeSettingsStore.Unset(LegacyOverlayKey);
        return new { ok = true, op = "reset", what = "overlay" };
    }

    static string AckKey(string checklistId, string itemId) =>
        $"{checklistId.Trim()}:{itemId.Trim()}".ToLowerInvariant();

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
            if (low.StartsWith("git.", StringComparison.Ordinal) || low.StartsWith("dap.", StringComparison.Ordinal)
                || low is "always")
                return low == "always" ? "always" : "state:" + low;
        }

        return s;
    }

    static string SanitizeId(string id)
    {
        var chars = id.Trim().ToLowerInvariant().Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    static ChecklistDef ToDef(OverlayChecklist c, bool builtin) =>
        new(
            c.Id,
            c.Title ?? c.Id,
            c.Links ?? [],
            (c.MemoryItems ?? []).Select(ToItem).ToArray(),
            (c.Items ?? []).Select(ToItem).ToArray(),
            builtin,
            c.Enabled);

    static ItemDef ToItem(OverlayItem i) =>
        new(i.Id, i.Kind ?? "do", i.Text ?? i.Id, i.Probe, i.Action, i.Required);

    static OverlayDoc LoadOverlay()
    {
        var raw = IdeSettingsStore.GetOrNull(OverlayKey)
                  ?? IdeSettingsStore.GetOrNull(LegacyOverlayKey);
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
        var raw = IdeSettingsStore.GetOrNull(AcksKey)
                  ?? IdeSettingsStore.GetOrNull(LegacyAcksKey);
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

    static object Err(string error, string hint) => new { ok = false, error, hint };

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

    sealed class OverlayDoc
    {
        public List<string>? Removed { get; set; }
        public List<string>? Disabled { get; set; }
        public List<string>? Enabled { get; set; }
        public Dictionary<string, List<string>>? ExtraLinks { get; set; }
        public Dictionary<string, List<string>>? RemovedLinks { get; set; }
        public List<OverlayChecklist>? Custom { get; set; }
    }

    sealed class OverlayChecklist
    {
        public string Id { get; set; } = "";
        public string? Title { get; set; }
        public List<string>? Links { get; set; }
        public List<OverlayItem>? MemoryItems { get; set; }
        public List<OverlayItem>? Items { get; set; }
        public bool Enabled { get; set; } = true;
    }

    sealed class OverlayItem
    {
        public string Id { get; set; } = "";
        public string? Kind { get; set; }
        public string? Text { get; set; }
        public string? Probe { get; set; }
        public string? Action { get; set; }
        public bool Required { get; set; } = true;
    }
}
