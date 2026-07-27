#nullable enable
using CdpMcp.Cockpit.Channels;

namespace CdpMcp.Cockpit.Channels;

/// <summary>Channel peel payload: deferred soft-organ wants (ADR 0036).</summary>
public readonly record struct DeferredSoftWantsPayload(
    bool Sys,
    bool Chk,
    bool Qrh,
    bool Alert,
    bool Problems,
    bool Plugins,
    bool Review);

/// <summary>IChannel for soft-organ deferral — peeks go-verb into typed wants.</summary>
public sealed class DeferredSoftOrganChannel : IChannel<string?, DeferredSoftWantsPayload>
{
    public DeferredSoftWantsPayload Build(in string? goVerb)
    {
        var g = goVerb;
        bool Take(params string[] aliases)
        {
            if (g is not { Length: > 0 })
                return false;
            foreach (var a in aliases)
            {
                if (g.Equals(a, StringComparison.OrdinalIgnoreCase))
                {
                    g = null;
                    return true;
                }
            }
            return false;
        }

        var sys = Take("sys");
        var chk = Take("chk", "ecl");
        var qrh = Take("qrh", "eqrh", "handbook");
        var alert = Take("alert", "eicas", "sa");
        var problems = Take("problems", "problem", "errlist", "errorlist", "err", "diags");
        var plugins = Take("plugins", "plugin", "vsix");
        var review = Take("review");
        return new DeferredSoftWantsPayload(sys, chk, qrh, alert, problems, plugins, review);
    }

    /// <summary>Same as Build but returns residual go-verb after peek.</summary>
    public (DeferredSoftWantsPayload Wants, string? ResidualGo) Peek(string? goVerb)
    {
        var g = goVerb;
        bool Take(params string[] aliases)
        {
            if (g is not { Length: > 0 })
                return false;
            foreach (var a in aliases)
            {
                if (g.Equals(a, StringComparison.OrdinalIgnoreCase))
                {
                    g = null;
                    return true;
                }
            }
            return false;
        }

        var wants = new DeferredSoftWantsPayload(
            Take("sys"),
            Take("chk", "ecl"),
            Take("qrh", "eqrh", "handbook"),
            Take("alert", "eicas", "sa"),
            Take("problems", "problem", "errlist", "errorlist", "err", "diags"),
            Take("plugins", "plugin", "vsix"),
            Take("review"));
        return (wants, g);
    }
}
