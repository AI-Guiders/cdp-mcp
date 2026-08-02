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
