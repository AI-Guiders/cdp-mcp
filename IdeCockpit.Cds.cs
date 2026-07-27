#nullable enable
using System.Text.Json;
using CdpMcp.Cockpit.Cds;
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp;

/// <summary>
/// CDS peel — attention routing via <see cref="AttentionCdsRouter"/> (ADR 0036).
/// </summary>
internal static partial class IdeCockpit
{
    static readonly AttentionCdsRouter AttentionRouter = new();

    /// <summary>Normalize mfd/go/page aliases before Channel/CCU dispatch.</summary>
    static (string Mfd, string? GoVerb, IReadOnlyDictionary<string, JsonElement> Args) NormalizeAttentionRouting(
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var mfdExplicit = OptString(args, "mfd") ?? OptString(args, "page");
        var snap = AttentionRouter.Route(new AttentionRoutingUnit.Input(
            MfdExplicit: mfdExplicit,
            GoVerb: OptString(args, "go") ?? OptString(args, "do"),
            SeatsMode: IdeDeskSeats.IsSeatsMode(),
            DefaultMfd: IdeSettingsHabitat.EffectiveDeskMfd()));

        var goVerb = snap.GoVerb;
        if (snap.DeskDetailNavForced)
            args = WithStringArg(args, "desk_detail", "nav");

        return (snap.Mfd, goVerb, args);
    }

    static string ResolveDeskDetail(IReadOnlyDictionary<string, JsonElement> args, string? focusId)
    {
        var raw = (OptString(args, "desk_detail") ?? OptString(args, "nav_detail") ?? "slim")
            .Trim().ToLowerInvariant();
        if (raw is "compact")
            raw = "slim";
        if (focusId is { Length: > 0 } && raw is "slim" or "omit")
            return "nav";
        if (raw is "slim" or "omit" or "nav" or "full")
            return raw is "omit" ? "slim" : raw;
        return "slim";
    }
}
