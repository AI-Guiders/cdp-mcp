#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: normalize MFD/go attention before channel dispatch (ADR 0097).</summary>
public sealed class AttentionRoutingUnit : ICockpitComputeUnit
{
    static readonly HashSet<string> MfdPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "nav", "sys", "chk", "ecl", "qrh", "gates"
    };

    public readonly record struct Input(
        string? MfdExplicit,
        string? GoVerb,
        bool SeatsMode,
        string? DefaultMfd);

    public readonly record struct Snapshot(
        string Mfd,
        string? GoVerb,
        bool DeskDetailNavForced) : ICockpitComputeUnitPayload;

    public Snapshot Compute(in Input input)
    {
        var mfd = (input.MfdExplicit
                   ?? (input.SeatsMode ? "nav" : input.DefaultMfd)
                   ?? "nav")
            .Trim().ToLowerInvariant();
        if (!MfdPages.Contains(mfd))
            mfd = "nav";

        var goVerb = input.GoVerb;
        var forceNav = false;

        if (goVerb is { Length: > 0 } && MfdPages.Contains(goVerb.Trim()))
        {
            var pageVerb = goVerb.Trim().ToLowerInvariant();
            mfd = pageVerb;
            if (pageVerb == "nav")
            {
                forceNav = true;
                goVerb = null;
            }
        }
        else if (goVerb is null
                 && input.MfdExplicit is not null
                 && mfd is "sys" or "chk" or "ecl" or "gates")
        {
            goVerb = mfd;
        }

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

        return new Snapshot(mfd, goVerb, forceNav);
    }
}
