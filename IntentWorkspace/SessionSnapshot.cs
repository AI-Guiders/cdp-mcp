using System.Text.Json;
using Cdp.Core;

namespace CdpMcp.IntentWorkspace;

internal static class SessionSnapshot
{
    public static string Capture(SessionContext session) => session.ToJson();

    public static void Apply(SessionContext session, string snapshotJson)
    {
        using var doc = JsonDocument.Parse(snapshotJson);
        var root = doc.RootElement;

        if (TryGetString(root, "phase") is { } phase && CdpEnumParse.TryParsePhase(phase, out var p))
            session.Phase = p;
        if (TryGetString(root, "object") is { } obj && CdpEnumParse.TryParseObject(obj, out var o))
            session.Object = o;

        if (root.TryGetProperty("intent", out var intentEl))
        {
            if (intentEl.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                session.Intent = null;
            else if (intentEl.GetString() is { Length: > 0 } intentStr
                     && CdpEnumParse.TryParseIntent(intentStr, out var i))
                session.Intent = i;
            else
                session.Intent = null;
        }

        session.Language = TryGetString(root, "language");
        session.ProjectRoot = TryGetString(root, "project_root");
        session.ProjectKind = TryGetString(root, "project_kind");
        session.SolutionOrProjectPath = TryGetString(root, "solution_or_project_path");
        session.TsConfigPath = TryGetString(root, "tsconfig_path");
        session.ScmRoot = TryGetString(root, "scm_root");
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
