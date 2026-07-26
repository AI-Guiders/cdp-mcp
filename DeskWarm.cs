#nullable enable
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Once-per-process cold desk warm — bookmark restore under the hood
/// so agents need not ritualize <c>cdp_open</c> / <c>cdp_restore</c> after remount.
/// </summary>
internal static class DeskWarm
{
    static int _consumed;

    /// <returns>true if this process may attempt auto-restore now.</returns>
    public static bool TryConsume() => Interlocked.Exchange(ref _consumed, 1) == 0;

    /// <summary>Test/reset helper — not for production call paths.</summary>
    internal static void ResetForTests() => Interlocked.Exchange(ref _consumed, 0);

    public static bool ShouldSkipForTool(
        string toolName,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> args)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return true;

        if (toolName.Equals("cdp_restore", StringComparison.OrdinalIgnoreCase))
            return true;

        if (toolName.Equals("cdp_project_close", StringComparison.OrdinalIgnoreCase))
            return true;

        if (toolName.Equals("cdp_open", StringComparison.OrdinalIgnoreCase))
        {
            if (HasNonEmptyString(args, "path"))
                return true;
            if (args.TryGetValue("recent_index", out var ri)
                && ri.ValueKind is System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.String)
                return true;
        }

        if (args.TryGetValue("no_restore", out var nrEl))
        {
            if (nrEl.ValueKind == System.Text.Json.JsonValueKind.True)
                return true;
            if (nrEl.ValueKind == System.Text.Json.JsonValueKind.String
                && bool.TryParse(nrEl.GetString(), out var nrBool) && nrBool)
                return true;
            if (nrEl.ValueKind == System.Text.Json.JsonValueKind.Number
                && nrEl.TryGetInt32(out var nrInt) && nrInt != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// If desk is cold, hydrate from bookmark once per process.
    /// Returns a pulse object for cockpit embed, or null when no attempt.
    /// </summary>
    public static object? TryWarm(
        string toolName,
        SessionContext session,
        DocumentBufferStore docStore,
        Func<string, ProjectOpenResult> detectOpen,
        Action? syncShellCwd,
        Action? notifyListChanged,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> args)
    {
        if (ShouldSkipForTool(toolName, args))
            return null;
        if (!string.IsNullOrWhiteSpace(session.ProjectRoot))
            return null;
        if (!TryConsume())
            return null;
        if (DeskBookmark.TryLoad() is null)
            return null;

        try
        {
            _ = DeskBookmark.Restore(
                session,
                docStore,
                detectOpen,
                syncShellCwd,
                notifyListChanged);
            return new
            {
                ok = true,
                source = "desk_bookmark",
                note = $"auto-restore on cold {toolName} (once/process)"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                source = "desk_bookmark",
                error = ex.Message
            };
        }
    }

    static bool HasNonEmptyString(
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> args,
        string key)
        => args.TryGetValue(key, out var el)
           && el.ValueKind == System.Text.Json.JsonValueKind.String
           && !string.IsNullOrWhiteSpace(el.GetString());
}
