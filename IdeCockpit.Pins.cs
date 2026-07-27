#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

/// <summary>Legacy tile pins + desk mutation / BuildTilesAsync peel.</summary>
internal static partial class IdeCockpit
{
    static readonly object PinGate = new();
    static List<string> StickyPins = [];

    /// <summary>
    /// Desk comfort: <c>locus=buffer:doc-N</c> + <c>go=reload|keep_disk|disk_peek</c>
    /// scopes to that file when <c>path=</c> / <c>go_args.path</c> omitted.
    /// </summary>
    static void EnsureDefaultLayoutFromSettings()
    {
        lock (PinGate)
        {
            if (StickyPins.Count > 0) return;
            var layout = IdeSettingsHabitat.EffectiveDeskLayout();
            if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout, out var preset))
                StickyPins = preset.Take(MaxTiles).ToList();
        }
    }

    /// <summary>Seats (default) or legacy tile pin mutations.</summary>
    static void ApplyDeskMutation(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (IdeDeskSeats.IsSeatsMode())
        {
            if (BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false)
                || BoolOr(args, "seat_clear", false) || BoolOr(args, "clear_seats", false))
            {
                IdeDeskSeats.Clear();
                return;
            }

            if (IdeDeskSeats.TryParseSeatAssignment(args, out var seat, out var organ)
                && seat is not null && organ is not null)
            {
                var pin = ResolvePinName(organ) ?? organ;
                IdeDeskSeats.TryPlaceExplicit(seat, pin);
                return;
            }

            var layout = OptString(args, "layout");
            if (layout is { Length: > 0 } && IdeDeskSeats.TryApplyPreset(layout))
                return;

            // pins= in seats mode: interpret as scan-order fill P,F,M (replace, not append).
            var pins = ParsePinList(args, "pins") ?? ParsePinList(args, "tiles");
            if (pins is { Count: > 0 })
            {
                IdeDeskSeats.Clear();
                for (var i = 0; i < Math.Min(pins.Count, IdeDeskSeats.Order.Length); i++)
                    IdeDeskSeats.TryPlaceExplicit(IdeDeskSeats.Order[i], pins[i]);
            }

            return;
        }

        ApplyPinMutation(args);
    }

    static string? ResolvePinName(string verb)
    {
        if (PinAliases.TryGetValue(verb, out var canon))
            return canon;
        return GoMap.ContainsKey(verb) ? verb : null;
    }

    /// <summary>Sticky report with no evidence → sit on plan (cheerful cold desk).</summary>
    static void CheerIdleReportSeat(SessionContext session)
    {
        var map = IdeDeskSeats.Snapshot();
        if (!map.TryGetValue("p", out var organ) || organ is not { Length: > 0 })
            return;
        if (CanonicalOrganPin(organ) is not "report")
            return;
        if (IdeReportBoard.HasEvidence(session))
            return;
        IdeDeskSeats.PlaceOrgan("plan");
    }

    static bool IsPlaceableOrgan(string pin) => DeskPlaceable.IsPlaceable(pin, PinAliases);

    static void ApplyPinMutation(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false))
        {
            lock (PinGate) StickyPins = [];
            return;
        }

        var layout = OptString(args, "layout");
        if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout.Trim(), out var preset))
        {
            lock (PinGate) StickyPins = preset.Take(MaxTiles).ToList();
            return;
        }

        var pins = ParsePinList(args, "pins") ?? ParsePinList(args, "tiles");
        if (pins is { Count: > 0 })
        {
            lock (PinGate) StickyPins = pins.Take(MaxTiles).ToList();
            return;
        }

        var add = ParsePinList(args, "pin");
        if (add is { Count: > 0 })
        {
            lock (PinGate)
            {
                foreach (var p in add)
                {
                    if (!StickyPins.Contains(p, StringComparer.OrdinalIgnoreCase) && StickyPins.Count < MaxTiles)
                        StickyPins.Add(p);
                }
            }
        }
    }

    static List<string> SnapshotPins()
    {
        lock (PinGate) return StickyPins.ToList();
    }

    static List<string> ResolveRequestedPins(IReadOnlyDictionary<string, JsonElement> args)
    {
        var layout = OptString(args, "layout");
        if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout.Trim(), out var preset))
            return preset.Take(MaxTiles).ToList();
        return ParsePinList(args, "pins") ?? ParsePinList(args, "tiles") ?? [];
    }

    static List<string>? ParsePinList(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;

        var raw = new List<string>();
        if (el.ValueKind == JsonValueKind.String)
        {
            raw.AddRange((el.GetString() ?? "")
                .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    raw.Add(s.Trim());
            }
        }
        else
            return null;

        var resolved = new List<string>();
        foreach (var r in raw)
        {
            if (PinAliases.TryGetValue(r, out var canon))
            {
                if (!resolved.Contains(canon, StringComparer.OrdinalIgnoreCase))
                    resolved.Add(canon);
            }
            else if (GoMap.ContainsKey(r) && !resolved.Contains(r, StringComparer.OrdinalIgnoreCase))
                resolved.Add(r);
        }

        return resolved.Count == 0 ? null : resolved;
    }

    static async Task<object> BuildTilesAsync(
        IReadOnlyList<string> pins,
        string? layout,
        string? fullPane,
        IReadOnlyDictionary<string, JsonElement> cockpitArgs,
        BufferSnap buffer,
        string? focusId,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var panes = new List<object>();
        foreach (var pin in pins.Take(MaxTiles))
        {
            var wantFull = fullPane is { Length: > 0 }
                && (string.Equals(fullPane, pin, StringComparison.OrdinalIgnoreCase)
                    || (PinAliases.TryGetValue(fullPane, out var fa)
                        && string.Equals(fa, pin, StringComparison.OrdinalIgnoreCase)));

            var tileArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var kv in cockpitArgs)
                tileArgs[kv.Key] = kv.Value;
            tileArgs["go_detail"] = JsonSerializer.SerializeToElement(wantFull ? "full" : "pulse");
            // Don't re-apply go= from parent into every pane.
            tileArgs.Remove("go");
            tileArgs.Remove("do");

            var pane = await DispatchGoAsync(pin, tileArgs, buffer, focusId, dispatch, cancellationToken)
                .ConfigureAwait(false);
            panes.Add(new
            {
                pin,
                full = wantFull,
                pane
            });
        }

        return new
        {
            ok = true,
            role = "tiles",
            layout,
            pins,
            count = panes.Count,
            panes,
            hint = "Human twin: code + browser side-by-side. Drill one pane: go=<pin> go_detail=full; or pane_full=<pin>."
        };
    }

}
