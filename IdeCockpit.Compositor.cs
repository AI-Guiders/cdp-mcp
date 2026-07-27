#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Compositor role for cockpit BuildAsync (CIDE ADR 0036 / arch_desk wire).
/// Projects CDS + channel goResult into desk surface JSON — not organ thrash.
/// </summary>
internal static partial class IdeCockpit
{
    /// <summary>Seats-mode desk surface (cockpit/v1.20): view once + seats + alert/pressure.</summary>
    private static string ComposeSeatsSurface(
        SessionContext session,
        string mfd,
        IReadOnlyList<SeatPane> seatPanes,
        bool wantPanes,
        string?[] seatPinList,
        object? goResult,
        object? warm,
        object next,
        object? focus,
        IdeAlertChannel.Snap alertSnap,
        string? thrashNote,
        IReadOnlyList<Locus> loci,
        string[] goVerbs,
        IReadOnlyDictionary<string, JsonElement> args,
        string? focusId)
    {
        var viewSlots = seatPanes
            .Select(s => new IdeDeskView.Slot(s.Seat, s.Organ, s.Empty, s.Ok, s.Line, s.Full))
            .ToArray();
        var view = IdeDeskView.Build(viewSlots);
        var seats = IdeDeskSeats.Card(
            seatPanes.Select(s => s.ToSlot()).ToList(),
            wantPanes ? seatPanes.Select(s => s.ToCard(true)).ToList() : null);

        var deskDetail = ResolveDeskDetail(args, focusId);
        var wantNav = deskDetail is "nav" or "full";
        var payload = new Dictionary<string, object?>
        {
            ["schema"] = SchemaVersion,
            ["ok"] = true,
            ["role"] = "desk",
            ["mode"] = "seats",
            ["view"] = view,
            ["mfd"] = mfd,
            ["mfd_note"] = "legacy alias — go=sys|chk|gates|nav (soft organs / desk_detail); no root page",
            ["session"] = SessionPulse(session),
            ["desk_detail"] = deskDetail,
            ["seats"] = seats,
            ["tiles"] = null,
            ["pins"] = seatPinList.Where(x => x is { Length: > 0 }).ToArray(),
            ["layouts"] = LayoutPresetIds,
            ["next"] = next,
            ["focus"] = focus,
            ["page"] = null,
            ["go"] = goResult,
            ["warm"] = warm,
            ["alert"] = IdeAlertChannel.PulseCard(alertSnap),
            ["instrument"] = InstrumentPulse(),
            ["pressure"] = IsPressureGoResult(goResult)
                ? null
                : IdePressureChannel.PulseCardOrNull(),
            ["thrash"] = thrashNote,
            ["hint"] = wantNav
                ? "Read view.banner / view.ascii first. Steer: cmd=\"go sa\" | layout=agent. " +
                  "C: pane_full= one dump; W: seats_detail=full spray."
                : "Slim desk (cockpit/v1.20): view + seats + next + alert(sa) + pressure?. " +
                  "go=sys|chk|pressure soft organs; desk_detail=nav for loci[]; cmd=sa|alert|pressure|probe|report|plan (CCL). " +
                  "Context W/C/A: A=pulse; C=go_detail=full|pane_full=; W=seats_detail=full."
        };
        if (wantNav)
        {
            payload["loci"] = loci.Select(l => l.Card()).ToArray();
            payload["go_verbs"] = goVerbs;
        }

        return JsonSerializer.Serialize(payload, Pretty);
    }

    /// <summary>Legacy tiles-mode desk surface.</summary>
    private static string ComposeTilesSurface(
        SessionContext session,
        string mfd,
        object? tiles,
        IReadOnlyList<string> pins,
        object? goResult,
        object? warm,
        object next,
        object? focus,
        IdeAlertChannel.Snap alertSnap,
        IReadOnlyList<Locus> loci,
        string[] goVerbs,
        IReadOnlyDictionary<string, JsonElement> args,
        string? focusId)
    {
        var deskDetail = ResolveDeskDetail(args, focusId);
        var wantNav = deskDetail is "nav" or "full";
        var payload = new Dictionary<string, object?>
        {
            ["schema"] = SchemaVersion,
            ["ok"] = true,
            ["role"] = "desk",
            ["mode"] = "tiles",
            ["mfd"] = mfd,
            ["mfd_note"] = "legacy alias — go=sys|chk|gates|nav; seats preferred; no root page",
            ["session"] = SessionPulse(session),
            ["desk_detail"] = deskDetail,
            ["seats"] = null,
            ["tiles"] = tiles,
            ["pins"] = pins.ToArray(),
            ["layouts"] = LayoutPresetIds,
            ["next"] = next,
            ["focus"] = focus,
            ["page"] = null,
            ["go"] = goResult,
            ["warm"] = warm,
            ["alert"] = IdeAlertChannel.PulseCard(alertSnap),
            ["hint"] = "desk.mode=tiles (legacy). Prefer seats. go=sys|chk soft organs; desk_detail=nav for loci."
        };
        if (wantNav)
        {
            payload["loci"] = loci.Select(l => l.Card()).ToArray();
            payload["go_verbs"] = goVerbs;
        }

        return JsonSerializer.Serialize(payload, Pretty);
    }
}
