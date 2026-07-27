#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// CDS (Cockpit Display System) — attention routing for agent desk (CIDE ADR 0036).
/// Answers «куда в кабине»: MFD page aliases, desk_detail, go= zone allowlist (see GoMap).
/// Orthogonal to IDS overlays (ADR 0079).
/// </summary>
internal static partial class IdeCockpit
{
    static readonly HashSet<string> MfdPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "nav", "sys", "chk", "ecl", "qrh", "gates"
    };

    /// <summary>Normalize mfd/go/page aliases before Channel/CCU dispatch.</summary>
    static (string Mfd, string? GoVerb, IReadOnlyDictionary<string, JsonElement> Args) NormalizeAttentionRouting(
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var mfdExplicit = OptString(args, "mfd") ?? OptString(args, "page");
        // Seats: desk.default_mfd deprecated — do not auto-steer organs from settings.
        var mfd = (mfdExplicit
                   ?? (IdeDeskSeats.IsSeatsMode() ? "nav" : IdeSettingsHabitat.EffectiveDeskMfd())
                   ?? "nav")
            .Trim().ToLowerInvariant();
        if (!MfdPages.Contains(mfd))
            mfd = "nav";

        var goVerb = OptString(args, "go") ?? OptString(args, "do");

        // Legacy MFD pages → seats: nav=desk_detail; sys|chk|gates=soft organs (not root page).
        if (goVerb is { Length: > 0 } && MfdPages.Contains(goVerb.Trim()))
        {
            var pageVerb = goVerb.Trim().ToLowerInvariant();
            mfd = pageVerb;
            if (pageVerb == "nav")
            {
                args = WithStringArg(args, "desk_detail", "nav");
                goVerb = null;
            }
            // else keep goVerb for soft-organ handlers below
        }
        else if (goVerb is null
                 && mfdExplicit is not null
                 && mfd is "sys" or "chk" or "ecl" or "gates")
        {
            // bare mfd=/page= (explicit) → same as go=
            goVerb = mfd;
        }

        // Soft tile / seat verbs: go=tiles|layout|seats|repl (no organ).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("tiles", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("layout", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("tile", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("seats", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("seat", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("repl", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("ccl", StringComparison.OrdinalIgnoreCase)))
        {
            goVerb = null;
        }

        return (mfd, goVerb, args);
    }

    static string ResolveDeskDetail(IReadOnlyDictionary<string, JsonElement> args, string? focusId)
    {
        var raw = (OptString(args, "desk_detail") ?? OptString(args, "nav_detail") ?? "slim")
            .Trim().ToLowerInvariant();
        if (raw is "compact")
            raw = "slim";
        // Focused locus needs the nav catalog.
        if (focusId is { Length: > 0 } && raw is "slim" or "omit")
            return "nav";
        if (raw is "slim" or "omit" or "nav" or "full")
            return raw is "omit" ? "slim" : raw;
        return "slim";
    }
}
