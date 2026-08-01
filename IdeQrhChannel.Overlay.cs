#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// eQRH overlay (<c>qrh.overlay</c>) — operator/agent pages without rebuilding Builtins.
/// Same pattern as ECL <c>ecl.overlay</c>: add/remove/disable; suggest rules are data.
/// </summary>
internal static partial class IdeQrhChannel
{
    public const string OverlayKey = "qrh.overlay";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Builtins + overlay custom − removed. Prefer over <see cref="Builtins"/> for open/search/suggest.</summary>
    public static IReadOnlyList<Page> AllPages()
    {
        var overlay = LoadOverlay();
        var map = new Dictionary<string, Page>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in Builtins())
            map[b.Id] = b;

        foreach (var id in overlay.Removed ?? [])
            map.Remove(id);

        foreach (var c in overlay.Custom ?? [])
        {
            if (string.IsNullOrWhiteSpace(c.Id))
                continue;
            if (overlay.Disabled?.Any(x => x.Equals(c.Id, StringComparison.OrdinalIgnoreCase)) == true)
                continue;
            map[c.Id] = ToPage(c);
        }

        foreach (var id in overlay.Disabled ?? [])
        {
            if (map.TryGetValue(id, out var cur) && cur.Builtin)
                map.Remove(id);
        }

        return map.Values.OrderBy(p => p.Shelf, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static void ApplyOverlaySuggest(
        List<(string Id, int Score)> hits,
        IdeChkChannel.ProbeCtx ctx,
        IdeChkChannel.Snap? ecl)
    {
        void Hit(string id, int score)
        {
            var i = hits.FindIndex(h => h.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (i < 0) hits.Add((id, score));
            else if (hits[i].Score < score) hits[i] = (id, score);
        }

        foreach (var p in AllPages())
        {
            if (p.Suggest is not { Count: > 0 })
                continue;
            foreach (var rule in p.Suggest)
            {
                if (rule.Score <= 0)
                    continue;
                var phaseHit = rule.Phases is { Count: > 0 }
                    && rule.Phases.Any(ph => ph.Equals(ctx.Phase, StringComparison.OrdinalIgnoreCase));
                var eclHit = ecl?.HotId is { Length: > 0 } hot
                    && rule.Ecl is { Count: > 0 }
                    && rule.Ecl.Any(e => e.Equals(hot, StringComparison.OrdinalIgnoreCase));
                if (phaseHit || eclHit)
                    Hit(p.Id, rule.Score);
            }
        }
    }

    static object DoAdd(Dictionary<string, JsonElement> args)
    {
        OverlayPage? fromJson = null;
        var pageRaw = Opt(args, "page") ?? Opt(args, "json");
        if (pageRaw is { Length: > 0 })
        {
            try
            {
                fromJson = JsonSerializer.Deserialize<OverlayPage>(pageRaw, JsonOpts);
            }
            catch (Exception ex)
            {
                return Err("page_json_invalid", $"page=/json= must be OverlayPage JSON ({ex.Message})");
            }
        }

        var id = SanitizeId(Opt(args, "id") ?? Opt(args, "name") ?? fromJson?.Id ?? "");
        if (id.Length == 0)
            return Err("id_required", "qrh add id=mine shelf=abnormal title=… | qrh add page={json}");

        var shelf = NormalizeShelf(Opt(args, "shelf") ?? fromJson?.Shelf ?? "abnormal");
        var title = Opt(args, "title") ?? fromJson?.Title ?? id;
        var condition = Opt(args, "condition") ?? fromJson?.Condition ?? title;

        var overlay = LoadOverlay();
        overlay.Custom ??= [];
        overlay.Removed?.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        overlay.Disabled?.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        overlay.Custom.RemoveAll(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        var page = fromJson ?? new OverlayPage();
        page.Id = id;
        page.Shelf = shelf;
        page.Title = title;
        page.Condition = condition;
        page.Signals ??= SplitList(Opt(args, "signals") ?? Opt(args, "signal"));
        page.MemoryItems ??= SplitList(Opt(args, "memory") ?? Opt(args, "memory_items"));
        page.Related ??= SplitList(Opt(args, "related"));
        page.PackAnchors ??= SplitList(Opt(args, "pack") ?? Opt(args, "pack_anchors"));
        page.LlmCue ??= Opt(args, "cue") ?? Opt(args, "llm_cue");
        page.Steps ??= ParseSteps(Opt(args, "steps"));
        page.Suggest ??= ParseSuggest(Opt(args, "suggest"));

        if (page.Signals is null || page.Signals.Count == 0)
            page.Signals = [id];
        if (page.MemoryItems is null)
            page.MemoryItems = [];
        if (page.Steps is null)
            page.Steps = [];
        if (page.Related is null)
            page.Related = [];
        if (page.PackAnchors is null)
            page.PackAnchors = [];

        overlay.Custom.Add(page);
        SaveOverlay(overlay);
        return new
        {
            ok = true,
            op = "add",
            id,
            shelf,
            title,
            pulse = $"qrh · added {id}",
            hint = "qrh open " + id + " — no rebuild; overlay in user prefs"
        };
    }

    static object DoRemove(Dictionary<string, JsonElement> args)
    {
        var id = SanitizeId(Opt(args, "id") ?? Opt(args, "name") ?? "");
        if (id.Length == 0)
            return Err("id_required", "qrh remove id=vague-criteria");

        var overlay = LoadOverlay();
        var customHit = overlay.Custom?.RemoveAll(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (Builtins().Any(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            overlay.Removed ??= [];
            if (!overlay.Removed.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                overlay.Removed.Add(id);
        }
        else if (!customHit)
            return Err("not_found", $"QRH page '{id}' not in catalog/overlay");

        SaveOverlay(overlay);
        return new { ok = true, op = "remove", id, pulse = $"qrh · removed {id}" };
    }

    static object DoEnable(Dictionary<string, JsonElement> args, bool enable)
    {
        var id = SanitizeId(Opt(args, "id") ?? Opt(args, "name") ?? "");
        if (id.Length == 0)
            return Err("id_required", enable ? "qrh enable id=…" : "qrh disable id=…");

        var overlay = LoadOverlay();
        overlay.Disabled ??= [];
        if (enable)
            overlay.Disabled.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        else if (!overlay.Disabled.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
            overlay.Disabled.Add(id);

        SaveOverlay(overlay);
        return new { ok = true, op = enable ? "enable" : "disable", id };
    }

    static object OverlayScene()
    {
        var o = LoadOverlay();
        return new
        {
            ok = true,
            go = "qrh",
            schema = SchemaVersion,
            mode = "overlay",
            pulse = $"qrh · overlay custom×{o.Custom?.Count ?? 0} removed×{o.Removed?.Count ?? 0}",
            key = OverlayKey,
            custom = o.Custom?.Select(c => c.Id).ToArray() ?? [],
            removed = o.Removed ?? [],
            disabled = o.Disabled ?? [],
            hint = "qrh add | qrh remove id= | qrh open …"
        };
    }
}
