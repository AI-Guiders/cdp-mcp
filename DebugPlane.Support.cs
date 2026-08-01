using System.Text.Json;
using Cdp.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;

namespace CdpMcp;

internal static partial class DebugPlane
{
    static async Task<string> ForwardAsync(
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        string underlying,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool fillPaths,
        CancellationToken cancellationToken)
    {
        if (!byDomain.TryGetValue(CdpDomains.Debug, out var mod) || !mod.IsEnabled)
            throw new InvalidOperationException("debug backend not mounted.");

        var mapped = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        mapped.Remove("op");
        if (fillPaths)
        {
            var (ws, target) = RequirePaths(session, args);
            if (!HasNonEmptyString(mapped, "workspace_path"))
                mapped["workspace_path"] = JsonSerializer.SerializeToElement(ws);
            if (!HasNonEmptyString(mapped, "target_path"))
                mapped["target_path"] = JsonSerializer.SerializeToElement(target);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var raw = await mod.CallAsync(underlying, mapped).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            schema = $"debug_{underlying.Replace("debug_", "", StringComparison.Ordinal)}/v0",
            ok = true,
            underlying,
            result = ResponseCaps.CapText(raw, max: 24_000)
        }, Pretty);
    }

    /// <summary>
    /// If DAP session is live for this workspace/target, push file breakpoints now
    /// (same grouping as launch). Otherwise applied on next launch/attach.
    /// </summary>
    static async Task<object> TryApplyLiveAsync(
        string workspacePath,
        string targetPath,
        IReadOnlyList<BreakpointsStorage.BreakpointEntry> breakpoints,
        CancellationToken cancellationToken)
    {
        var client = DebugSession.CurrentClient;
        if (client is null)
            return new { applied = false, reason = "no_active_dap; will apply on next launch/attach" };

        var wsMatch = DebugSession.WorkspacePath is { Length: > 0 } w
            && string.Equals(Path.GetFullPath(w), Path.GetFullPath(workspacePath), StringComparison.OrdinalIgnoreCase);
        var targetMatch = DebugSession.TargetPath is { Length: > 0 } t
            && string.Equals(Path.GetFullPath(t), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase);
        if (!wsMatch || !targetMatch)
        {
            return new
            {
                applied = false,
                reason = "active_dap_other_target",
                active_workspace = DebugSession.WorkspacePath,
                active_target = DebugSession.TargetPath
            };
        }

        try
        {
            var byFile = breakpoints
                .GroupBy(b => Path.GetFullPath(b.File), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var g in byFile)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var list = g.Select(b => (b.Line, b.Condition)).ToList();
                await client.SetBreakpointsAsync(g.Key, list, cancellationToken).ConfigureAwait(false);
            }

            return new { applied = true, files = byFile.Count, breakpoints = breakpoints.Count };
        }
        catch (Exception ex)
        {
            return new { applied = false, reason = "dap_setBreakpoints_failed", error = ex.Message };
        }
    }

    static object WireBp(BreakpointsStorage.BreakpointEntry b) => new
    {
        path = b.File,
        line = b.Line,
        condition = b.Condition
    };

    static (string workspace, string target) RequirePaths(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool targetOptional = false)
    {
        var (ws, target, note) = TryResolvePaths(session, emptyArgs: false, args);
        if (ws is not { Length: > 0 })
            throw new ArgumentException("workspace_path required (or cdp_open first). " + note);
        if (!targetOptional && target is not { Length: > 0 })
            throw new ArgumentException(
                "target_path required (or cdp_open so session.solution_or_project_path is set).");
        return (ws, target ?? "");
    }

    static (string? workspace, string? target, string? note) TryResolvePaths(
        SessionContext session,
        bool emptyArgs,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        string? ws = null;
        string? target = null;
        if (!emptyArgs && args is not null)
        {
            ws = OptString(args, "workspace_path");
            target = OptString(args, "target_path");
        }

        ws ??= session.ProjectRoot ?? session.ScmRoot;
        target ??= session.SolutionOrProjectPath;
        string? note = null;
        if (ws is null)
            note = "No session project — call cdp_open or pass workspace_path.";
        return (ws, target, note);
    }

    static bool HasNonEmptyString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el)
        && el.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(el.GetString());

    static string? PropString(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out var el)
        && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static bool TryPropInt(JsonElement item, string name, out int value)
    {
        value = 0;
        return item.ValueKind == JsonValueKind.Object
               && item.TryGetProperty(name, out var el)
               && el.TryGetInt32(out value);
    }

    static string RequireString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.GetString() is not { Length: > 0 } s)
            throw new ArgumentException($"{key} (string) is required.");
        return s;
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) ? el.GetString() : null;

    static int RequireInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || !el.TryGetInt32(out var n))
            throw new ArgumentException($"{key} (integer) is required.");
        return n;
    }
}
