#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeCrmChannel
{
    static CrmSnap? Read(SessionContext session)
    {
        var path = LatestPath(session);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CrmSnap>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    static void Write(SessionContext session, CrmSnap snap)
    {
        var dir = InboxDir(session);
        Directory.CreateDirectory(dir);
        var latest = Path.Combine(dir, "LATEST.json");
        var stamped = Path.Combine(dir, $"crm-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{snap.CallId}.json");
        var json = JsonSerializer.Serialize(snap, Pretty);
        File.WriteAllText(latest, json);
        File.WriteAllText(stamped, json);
    }

    static string LatestPath(SessionContext session) => Path.Combine(InboxDir(session), "LATEST.json");

    static string InboxDir(SessionContext session)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is { Length: > 0 })
            return Path.GetFullPath(Path.Combine(root, ".cdp", "crm"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "crm");
    }

}
