#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Composition;

namespace CdpMcp;

/// <summary>
/// Compositor role for cockpit BuildAsync (CIDE ADR 0036 / arch_desk wire).
/// Seats surface via <see cref="SeatsSurfaceCompositor"/> — not Avalonia.
/// </summary>
internal static partial class IdeCockpit
{
    static readonly SeatsSurfaceCompositor SeatsCompositor = new();

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

        var decision = ResolveDeskDetailSnap(args, focusId);
        var scene = new SeatsSurfaceScene(
            SchemaVersion: SchemaVersion,
            Mfd: mfd,
            View: view,
            Seats: seats,
            Session: SessionPulse(session),
            Instrument: InstrumentPulse(),
            Alert: IdeAlertChannel.PulseCard(alertSnap),
            Pressure: IsPressureGoResult(goResult) ? null : IdePressureChannel.PulseCardOrNull(),
            Next: next,
            Focus: focus,
            Go: goResult,
            Warm: warm,
            Pins: seatPinList,
            Layouts: LayoutPresetIds,
            ThrashNote: thrashNote,
            Loci: decision.WantNav ? loci.Select(l => l.Card()).ToArray() : null,
            GoVerbs: decision.WantNav ? goVerbs : null);

        var payload = SeatsCompositor.Compose(
            scene,
            new SeatsSurfacePayload(seatPanes.Count),
            decision);
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
