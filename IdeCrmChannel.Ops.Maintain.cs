#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeCrmChannel
{
    static object Last(SessionContext session)
    {
        var snap = Read(session);
        return new
        {
            ok = snap is not null,
            schema = SchemaVersion,
            op = "last",
            pulse = PulseLine(snap),
            call = snap is null ? null : Card(snap)
        };
    }

    static object Clear(SessionContext session)
    {
        var path = LatestPath(session);
        if (File.Exists(path))
            File.Delete(path);
        PublishGlass(session);
        return new { ok = true, schema = SchemaVersion, op = "clear", pulse = "crm · idle" };
    }

    static object? TryBridgePlan(
        SessionContext session,
        IntentWorkspaceStore? store,
        IntentWorkspaceState? state,
        bool reject)
    {
        if (store is null || state is null)
            return null;
        try
        {
            return IdePlanPromote.Confirm(store, state, session.ProjectRoot, null, null, reject);
        }
        catch
        {
            return null;
        }
    }
}
