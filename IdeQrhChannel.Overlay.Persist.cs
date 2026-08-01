#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>QRH overlay persist + DTOs (≤ADX soft-warn peel).</summary>
internal static partial class IdeQrhChannel
{
    static Page ToPage(OverlayPage c) =>
        new(
            c.Id,
            NormalizeShelf(c.Shelf ?? "abnormal"),
            c.Title ?? c.Id,
            c.Condition ?? c.Title ?? c.Id,
            c.Signals ?? [],
            c.MemoryItems ?? [],
            (c.Steps ?? []).Select(s => new Step(s.Text ?? "", s.Go, s.Action)).ToArray(),
            c.Related ?? [],
            c.PackAnchors ?? [],
            c.LlmCue,
            (c.Suggest ?? []).Select(s => new SuggestRule(s.Phases, s.Ecl, s.Score)).ToArray(),
            Builtin: false);

    static OverlayDoc LoadOverlay()
    {
        IdeSettingsStore.EnsureLoaded();
        var raw = IdeSettingsStore.GetOrNull(OverlayKey);
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
        IdeSettingsStore.EnsureLoaded();
        IdeSettingsStore.Set(OverlayKey, JsonSerializer.Serialize(doc, JsonOpts));
    }

    static string SanitizeId(string id)
    {
        var chars = id.Trim().ToLowerInvariant().Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    static string NormalizeShelf(string shelf)
    {
        var s = shelf.Trim().ToLowerInvariant();
        return s is "systems" or "abnormal" or "emergency" ? s : "abnormal";
    }

    static List<string> SplitList(string? raw)
    {
        if (raw is not { Length: > 0 })
            return [];
        return raw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToList();
    }

    static List<OverlayStep>? ParseSteps(string? raw)
    {
        if (raw is not { Length: > 0 })
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<OverlayStep>>(raw, JsonOpts);
        }
        catch
        {
            return raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => new OverlayStep { Text = t })
                .ToList();
        }
    }

    static List<OverlaySuggest>? ParseSuggest(string? raw)
    {
        if (raw is not { Length: > 0 })
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<OverlaySuggest>>(raw, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    sealed class OverlayDoc
    {
        public List<string>? Removed { get; set; }
        public List<string>? Disabled { get; set; }
        public List<OverlayPage>? Custom { get; set; }
    }

    sealed class OverlayPage
    {
        public string Id { get; set; } = "";
        public string? Shelf { get; set; }
        public string? Title { get; set; }
        public string? Condition { get; set; }
        public List<string>? Signals { get; set; }
        public List<string>? MemoryItems { get; set; }
        public List<OverlayStep>? Steps { get; set; }
        public List<string>? Related { get; set; }
        public List<string>? PackAnchors { get; set; }
        public string? LlmCue { get; set; }
        public List<OverlaySuggest>? Suggest { get; set; }
    }

    sealed class OverlayStep
    {
        public string? Text { get; set; }
        public string? Go { get; set; }
        public string? Action { get; set; }
    }

    sealed class OverlaySuggest
    {
        public List<string>? Phases { get; set; }
        public List<string>? Ecl { get; set; }
        public int Score { get; set; }
    }
}
