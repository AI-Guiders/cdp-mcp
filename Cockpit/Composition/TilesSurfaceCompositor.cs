#nullable enable

namespace CdpMcp.Cockpit.Composition;

/// <summary>ISurfaceCompositor for tiles desk JSON (ADR 0036) — legacy mode.</summary>
public sealed class TilesSurfaceCompositor
    : ISurfaceCompositor<TilesSurfaceScene, TilesSurfacePayload, DeskDetailDecision, Dictionary<string, object?>>
{
    public Dictionary<string, object?> Compose(
        TilesSurfaceScene scene,
        TilesSurfacePayload payload,
        in DeskDetailDecision decision)
    {
        _ = payload;
        var dict = new Dictionary<string, object?>
        {
            ["schema"] = scene.SchemaVersion,
            ["ok"] = true,
            ["role"] = "desk",
            ["mode"] = "tiles",
            ["mfd"] = scene.Mfd,
            ["mfd_note"] = "legacy alias — go=sys|chk|gates|nav; seats preferred; no root page",
            ["session"] = scene.Session,
            ["desk_detail"] = decision.DeskDetail,
            ["seats"] = null,
            ["tiles"] = scene.Tiles,
            ["pins"] = scene.Pins,
            ["layouts"] = scene.Layouts,
            ["next"] = scene.Next,
            ["focus"] = scene.Focus,
            ["page"] = null,
            ["go"] = scene.Go,
            ["warm"] = scene.Warm,
            ["alert"] = scene.Alert,
            ["hint"] = "desk.mode=tiles (legacy). Prefer seats. go=sys|chk soft organs; desk_detail=nav for loci."
        };
        if (decision.WantNav)
        {
            dict["loci"] = scene.Loci;
            dict["go_verbs"] = scene.GoVerbs;
        }

        return dict;
    }
}
