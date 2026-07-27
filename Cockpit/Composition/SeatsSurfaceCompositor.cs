#nullable enable
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.Cockpit.DataBus;

namespace CdpMcp.Cockpit.Composition;

/// <summary>Inputs already projected by peels — compositor assembles seats surface DTO.</summary>
public readonly record struct SeatsSurfaceScene(
    string SchemaVersion,
    string Mfd,
    object View,
    object Seats,
    object Session,
    object? Instrument,
    object? Alert,
    object? Pressure,
    object Next,
    object? Focus,
    object? Go,
    object? Warm,
    string?[] Pins,
    string[] Layouts,
    string? ThrashNote,
    object? Loci,
    string[]? GoVerbs);

public readonly record struct SeatsSurfacePayload(int SeatCount);

/// <summary>ISurfaceCompositor for seats desk JSON (ADR 0036) — no Avalonia.</summary>
public sealed class SeatsSurfaceCompositor
    : ISurfaceCompositor<SeatsSurfaceScene, SeatsSurfacePayload, DeskDetailUnit.Snapshot, Dictionary<string, object?>>
{
    public Dictionary<string, object?> Compose(
        SeatsSurfaceScene scene,
        SeatsSurfacePayload payload,
        in DeskDetailUnit.Snapshot decision)
    {
        var dict = new Dictionary<string, object?>
        {
            ["schema"] = scene.SchemaVersion,
            ["ok"] = true,
            ["role"] = "desk",
            ["mode"] = "seats",
            ["view"] = scene.View,
            ["mfd"] = scene.Mfd,
            ["mfd_note"] = "legacy alias — go=sys|chk|gates|nav (soft organs / desk_detail); no root page",
            ["session"] = scene.Session,
            ["desk_detail"] = decision.DeskDetail,
            ["seats"] = scene.Seats,
            ["tiles"] = null,
            ["pins"] = scene.Pins.Where(x => x is { Length: > 0 }).ToArray(),
            ["layouts"] = scene.Layouts,
            ["next"] = scene.Next,
            ["focus"] = scene.Focus,
            ["page"] = null,
            ["go"] = scene.Go,
            ["warm"] = scene.Warm,
            ["alert"] = scene.Alert,
            ["instrument"] = scene.Instrument,
            ["pressure"] = scene.Pressure,
            ["thrash"] = scene.ThrashNote,
            ["hint"] = decision.WantNav
                ? "Read view.banner / view.ascii first. Steer: cmd=\"go sa\" | layout=agent. " +
                  "C: pane_full= one dump; W: seats_detail=full spray."
                : "Slim desk (cockpit/v1.20): view + seats + next + alert(sa) + pressure?. " +
                  "go=sys|chk|pressure soft organs; desk_detail=nav for loci[]; cmd=sa|alert|pressure|probe|report|plan (CCL). " +
                  "Context W/C/A: A=pulse; C=go_detail=full|pane_full=; W=seats_detail=full."
        };
        if (decision.WantNav)
        {
            dict["loci"] = scene.Loci;
            dict["go_verbs"] = scene.GoVerbs;
        }

        DeskDataBusHost.Current.Publish(new DeskSurfaceBuiltEvent(
            Mode: "seats",
            SeatCount: payload.SeatCount,
            Go: scene.Go?.GetType().Name,
            Utc: DateTimeOffset.UtcNow));

        return dict;
    }
}
