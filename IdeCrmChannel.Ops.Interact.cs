#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeCrmChannel
{
    static object Call(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var ask = Opt(args, "ask") ?? Opt(args, "what") ?? Opt(args, "text") ?? "Confirm approach";
        var kind = Opt(args, "kind") ?? Opt(args, "ref_kind") ?? "general";
        var refId = Opt(args, "ref") ?? Opt(args, "ref_id") ?? Opt(args, "plan_id")
                    ?? Guid.NewGuid().ToString("N")[..12];
        var snap = new CrmSnap(
            SchemaVersion,
            Guid.NewGuid().ToString("N")[..12],
            Awaiting,
            null,
            kind,
            refId,
            ask.Trim(),
            DateTime.UtcNow,
            null,
            null);
        Write(session, snap);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            op = "call",
            pulse = PulseLine(snap),
            call = Card(snap),
            chat = $"CRM awaiting: {ask.Trim()}",
            next = BuildNext(snap),
            hint = "Human responds via cockpit/REPL CRM codes — do not negotiate in chat."
        };
    }

    static object Respond(
        SessionContext session,
        IntentWorkspaceStore? store,
        IntentWorkspaceState? state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var code = NormCode(Opt(args, "code") ?? Opt(args, "callout") ?? Opt(args, "response") ?? Opt(args, "say"));
        if (code is null)
            return Err("code_required", "crm respond code=approved|go_around|stabilized|hold|…");

        var why = Opt(args, "why");
        if (why is { Length: > 80 })
            why = why[..80];

        var prev = Read(session);
        var snap = (prev ?? new CrmSnap(
            SchemaVersion,
            Guid.NewGuid().ToString("N")[..12],
            Awaiting,
            null,
            "general",
            "adhoc",
            "callout",
            DateTime.UtcNow,
            null,
            null)) with
        {
            Status = code,
            Callout = code,
            Why = why,
            ResolvedUtc = DateTime.UtcNow
        };
        Write(session, snap);

        object? planBridge = null;
        if (code is "approved" or "go_around" or "negative" or "unable")
            planBridge = TryBridgePlan(session, store, state, reject: code is not "approved");

        return new
        {
            ok = true,
            schema = SchemaVersion,
            op = "respond",
            pulse = PulseLine(snap),
            call = Card(snap),
            plan = planBridge,
            chat = $"CRM {code}" + (why is { Length: > 0 } ? $" · {why}" : ""),
            next = BuildNext(snap),
            hint = "Gate speech done in SSOT — continue from pulse, not chat negotiation."
        };
    }
}
