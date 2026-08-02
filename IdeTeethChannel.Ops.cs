#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeTeethChannel
{
    static object Scene(IReadOnlyDictionary<string, JsonElement> args)
    {
        var cdtLive = OptBool(args, "cdt") ?? false;
        var now = BuildNow(cdtLive);
        var last = IdeTeethTape.ReadTail(12);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = BuildPulse(now),
            now = NowDto(now),
            last = last.Select(IdeTeethTape.Slim).ToArray(),
            explain = BuildExplain(now, last),
            tape = IdeTeethTape.TapePath,
            next = new object[]
            {
                new
                {
                    go = GoName,
                    label = "Tail",
                    why = "op=tail limit=40"
                },
                new
                {
                    go = GoName,
                    label = "Explain",
                    why = "op=explain — last wake/busy/away"
                },
                new
                {
                    go = "ignite_desk",
                    label = "Ignite list",
                    why = "op=list arms"
                },
                new
                {
                    go = "health",
                    label = "Health",
                    why = "teeth_pulse on cdp_health"
                }
            },
            hint = "Guest-host teeth: CDT/Stop, remount·oom delivery, OOM tooth, partner away/here. " + "First away = status; still away after ~60s → autonomy. cdt=true for live sample."
        };
    }

    static object Tail(IReadOnlyDictionary<string, JsonElement> args)
    {
        var limit = OptInt(args, "limit") ?? 40;
        var events = IdeTeethTape.ReadTail(limit);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "tail",
            go = GoName,
            count = events.Count,
            events = events.Select(IdeTeethTape.Slim).ToArray(),
            tape = IdeTeethTape.TapePath,
            hint = "Newest at end. Prefer op=scene for pulse+now+explain."
        };
    }

    static object Explain(IReadOnlyDictionary<string, JsonElement> args)
    {
        var armId = Opt(args, "id") ?? Opt(args, "arm");
        var now = BuildNow(cdtLive: false);
        var last = IdeTeethTape.ReadTail(40);
        var focused = string.IsNullOrWhiteSpace(armId) ? last.LastOrDefault(e => e.Kind.StartsWith("wake_", StringComparison.OrdinalIgnoreCase) || e.Kind is "partner_away" or "partner_here" or "partner_away_escalate" || e.Kind is "oom_dialog" or "cdt_edge" or "deploy_hard") : last.LastOrDefault(e => string.Equals(e.ArmId, armId, StringComparison.OrdinalIgnoreCase));
        var text = focused is null ? BuildExplain(now, last) : ExplainEvent(focused, now);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "explain",
            go = GoName,
            explain = text,
            focus = focused is null ? null : IdeTeethTape.Slim(focused),
            now = NowDto(now),
            pulse = BuildPulse(now)
        };
    }
}