#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>HealthJson extract helpers (method_lines peel under warn70).</summary>
internal static partial class MetaDispatch
{
    static object? TryExplainTool(MetaDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        if (callArgs.TryGetValue("explain_tool", out var eht) && eht.GetString() is { Length: > 0 } en)
            return SessionPlane.ExplainTool(en, d.Session, d.ByDomain, d.AllAffordances);
        return null;
    }

    static DateTimeOffset? TryGetExeBuildUtc(string? exePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                return File.GetLastWriteTimeUtc(exePath);
        }
        catch { /* ignore */ }

        return null;
    }

    static object? TryReadPendingUpdate(string? exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (dir is not { Length: > 0 })
                return null;

            var pendingPath = Path.Combine(dir, "cdp-pending-update.json");
            if (!File.Exists(pendingPath))
                return null;

            return JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(pendingPath));
        }
        catch
        {
            return new { ok = false, error = "pending_update_unreadable" };
        }
    }
}
