#nullable enable
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeCrmChannel
{
    static object Scene(SessionContext session)
    {
        var snap = Read(session);
        var pulse = PulseLine(snap);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "crm",
            go = "crm",
            tool = ToolName,
            detail = "slim",
            pulse,
            status = snap?.Status ?? "idle",
            call = snap is null ? null : Card(snap),
            lexicon = Lexicon,
            next = BuildNext(snap),
            hint = "Operator: cmd=approved|stabilized|go around|hold|…. Agent: op=call then poll scene — no reject essays."
        };
    }
}
