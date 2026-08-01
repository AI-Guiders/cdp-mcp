using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>Seat placement, persist, and Card for desk seats.</summary>
internal static partial class IdeDeskSeats
{
    public static string? ResolveSeatForOrgan(string organPin)
    {
        var pin = CanonicalOrganPin(organPin);
        foreach (var key in PolicyOverrideKeys(pin))
        {
            var over = IdeSettingsStore.GetOrNull($"desk.seat.organ.{key}");
            if (over is { Length: > 0 } && NormalizeSeatId(over) is { } s)
                return s;
        }

        if (DefaultPolicy.TryGetValue(pin, out var seat))
            return seat;

        return "m";
    }

    static IEnumerable<string> PolicyOverrideKeys(string pin)
    {
        yield return pin;
        if (pin.EndsWith("_scene", StringComparison.OrdinalIgnoreCase))
            yield return pin[..^"_scene".Length];
    }

    public static string CanonicalOrganPin(string organPin) =>
        IdeCockpit.CanonicalOrganPin(organPin);

    public static string? PlaceOrgan(string organPin)
    {
        var pin = CanonicalOrganPin(organPin);
        if (pin.Length == 0) return null;
        var seat = ResolveSeatForOrgan(pin);
        if (seat is null) return null;
        lock (Gate)
        {
            PlaceUnlocked(seat, pin);
            PersistUnlocked();
        }

        return seat;
    }

    public static bool TryPlaceExplicit(string seatRaw, string organPin)
    {
        var seat = NormalizeSeatId(seatRaw);
        if (seat is null || string.IsNullOrWhiteSpace(organPin))
            return false;
        lock (Gate)
        {
            PlaceUnlocked(seat, CanonicalOrganPin(organPin));
            PersistUnlocked();
        }

        return true;
    }

    public static string? NormalizeSeatId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "p" or "pfd" or "left" => "p",
            "f" or "fwd" or "forward" or "centre" or "center" or "main" => "forward",
            "m" or "mfd" or "right" => "m",
            _ => null
        };
    }

    static void PlaceUnlocked(string seat, string? pin)
    {
        if (!Sticky.ContainsKey(seat)) return;
        Sticky[seat] = string.IsNullOrWhiteSpace(pin) ? null : CanonicalOrganPin(pin);
    }

    static bool TryLoadUnlocked()
    {
        if (Store is null)
            return false;
        try
        {
            if (!Store.DeskSeatsTryLoad(Sticky))
                return false;
            foreach (var seat in Order)
            {
                if (Sticky.TryGetValue(seat, out var pin) && pin is { Length: > 0 })
                    Sticky[seat] = CanonicalOrganPin(pin);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    static void PersistUnlocked()
    {
        if (Store is not null)
        {
            try
            {
                Store.DeskSeatsSave(Order.ToDictionary(s => s, s => Sticky[s], StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                // Desk must not die on IO; next mutation retries.
            }
        }

        // Dual-cockpit glass: publish even when WitDB store is unbound (tests / early boot).
        try
        {
            CideSeatsLatch.Publish(Order.ToDictionary(s => s, s => Sticky[s], StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            /* best-effort */
        }
    }

    public static object Card(IReadOnlyList<object> slots, IReadOnlyList<object>? panes, object? view = null)
    {
        // Root cockpit already carries view — omit duplicate unless caller embeds.
        if (view is null)
        {
            return new
            {
                ok = true,
                role = SchemaRole,
                scan = "p→forward→m",
                order = Order,
                map = Snapshot(),
                persist = "witdb:desk_seats",
                slots,
                panes,
                count = slots.Count,
                hint =
                    "Read root view.banner / view.ascii. Seats sticky in WitDB. " +
                    "panes when seats_detail=full or pane_full=. go=browser → M replaces."
            };
        }

        return new
        {
            ok = true,
            role = SchemaRole,
            scan = "p→forward→m",
            order = Order,
            map = Snapshot(),
            persist = "witdb:desk_seats",
            view,
            slots,
            panes,
            count = slots.Count,
            hint =
                "Read view.banner / view.ascii. Seats sticky in WitDB (survive remount). " +
                "panes when seats_detail=full or pane_full=. go=browser → M replaces."
        };
    }

    public static bool TryParseSeatAssignment(IReadOnlyDictionary<string, JsonElement> args, out string? seat, out string? organ)
    {
        seat = NormalizeSeatId(Opt(args, "seat"));
        organ = Opt(args, "organ") ?? Opt(args, "pin");
        if (seat is null || organ is null || organ.Length == 0)
            return false;
        return true;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
