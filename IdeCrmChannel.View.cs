#nullable enable

namespace CdpMcp;

internal static partial class IdeCrmChannel
{
    static object[] BuildNext(CrmSnap? snap)
    {
        if (snap is null || !string.Equals(snap.Status, Awaiting, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new { go = "crm", label = "Call", why = "op=call ask=…" },
                new { go = "plan", label = "Share plan", why = "ask=confirm → CRM awaiting" }
            ];
        }

        // Operator panel strip (desk) — closed codes only.
        return
        [
            new { go = "crm", label = "Approved", why = "op=respond code=approved" },
            new { go = "crm", label = "Stabilized", why = "op=respond code=stabilized" },
            new { go = "crm", label = "Go Around", why = "op=respond code=go_around" },
            new { go = "crm", label = "Hold", why = "op=respond code=hold" },
            new { go = "crm", label = "Unable", why = "op=respond code=unable" },
            new { go = "crm", label = "Say Again", why = "op=respond code=say_again" }
        ];
    }

    static string PulseLine(CrmSnap? snap)
    {
        if (snap is null)
            return "crm · idle";
        if (string.Equals(snap.Status, Awaiting, StringComparison.OrdinalIgnoreCase))
            return $"crm · AWAITING · {snap.Kind}:{snap.RefId}";
        return $"crm · {snap.Callout ?? snap.Status} · {snap.Kind}:{snap.RefId}";
    }

    static object Card(CrmSnap snap) => new
    {
        call_id = snap.CallId,
        status = snap.Status,
        callout = snap.Callout,
        kind = snap.Kind,
        ref_id = snap.RefId,
        ask = snap.Ask,
        why = snap.Why,
        opened_utc = snap.OpenedUtc,
        resolved_utc = snap.ResolvedUtc
    };

}
