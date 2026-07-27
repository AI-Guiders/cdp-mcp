#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>BuildAsync ingress peel — transport, REPL, attention routing, desk prepare.</summary>
internal static partial class IdeCockpit
{
    readonly record struct BuildIngress(
        IReadOnlyDictionary<string, JsonElement> Args,
        object? ReplDirect,
        string? FocusId,
        bool IncludeSubmodules,
        string Mfd,
        string? GoVerb);

    static BuildIngress PrepareBuildIngress(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        object? replDirect = null;
        var transport = IngestCockpitRequest(args);
        args = transport.Args;
        var cmdLine = transport.CmdLine;
        if (cmdLine is { Length: > 0 })
        {
            var applied = IdeRepl.Apply(cmdLine, args);
            if (applied is { } a)
            {
                args = a.Args;
                replDirect = a.Direct;
            }
        }

        var focusId = OptString(args, "locus") ?? OptString(args, "focus");
        var includeSubmodules = BoolOr(args, "include_submodules", false);
        string mfd;
        string? goVerb;
        (mfd, goVerb, args) = NormalizeAttentionRouting(args);

        ApplyDeskMutation(args);
        var deskCleared = BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false)
            || BoolOr(args, "seat_clear", false) || BoolOr(args, "clear_seats", false);
        if (IdeDeskSeats.IsSeatsMode())
        {
            if (!deskCleared)
                IdeDeskSeats.EnsureDefaultsFromSettings();
            CheerIdleReportSeat(session);
        }
        else
            EnsureDefaultLayoutFromSettings();

        return new BuildIngress(args, replDirect, focusId, includeSubmodules, mfd, goVerb);
    }
}
