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
    /// Файлы, которые реально ушли в текущую DAP-сессию. Нужны, чтобы снимать точки с файлов,
    /// выпавших из нового списка: DAP setBreakpoints заменяет набор только у присланного файла,
    /// поэтому без этого в адаптере остаются точки, которых в хранилище уже нет.
    /// </summary>
    static DapClient? LiveBpClient;
    static readonly HashSet<string> LiveBpFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Новая DAP-сессия — прежний список живых файлов не переносим.</summary>
    static void ResetLiveBpIfNewSession(DapClient client)
    {
        if (ReferenceEquals(LiveBpClient, client))
            return;
        LiveBpClient = client;
        LiveBpFiles.Clear();
    }

    /// <summary>
    /// If DAP session is live for this workspace/target, push file breakpoints now
    /// (same grouping as launch). Otherwise applied on next launch/attach.
    /// Files that dropped out of the list are cleared in the adapter — otherwise the live set drifts from storage.
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
            ResetLiveBpIfNewSession(client);

            var byFile = breakpoints
                .GroupBy(b => Path.GetFullPath(b.File), StringComparer.OrdinalIgnoreCase)
                .ToList();
            var wanted = new HashSet<string>(byFile.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

            // Снять точки с файлов, которые были в сессии и выпали из нового списка.
            var cleared = new List<string>();
            foreach (var stale in LiveBpFiles.Where(file => !wanted.Contains(file)).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await client.SetBreakpointsAsync(stale, Array.Empty<(int, string?)>(), cancellationToken).ConfigureAwait(false);
                LiveBpFiles.Remove(stale);
                cleared.Add(stale);
            }

            var verifiedCount = 0;
            var noVerdict = 0;
            var unverified = new List<object>();
            foreach (var group in byFile)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var list = group.Select(b => (b.Line, b.Condition)).ToList();
                var verdicts = await client.SetBreakpointsWithVerdictsAsync(group.Key, list, cancellationToken).ConfigureAwait(false);
                if (verdicts.Count == 0)
                {
                    // Адаптер не вернул вердикт: это «неизвестно», а не «связано».
                    noVerdict += list.Count;
                }
                else
                {
                    foreach (var verdict in verdicts)
                    {
                        if (verdict.Verified)
                            verifiedCount++;
                        else
                            unverified.Add(new { file = group.Key, line = verdict.Line, message = verdict.Message });
                    }
                }

                LiveBpFiles.Add(group.Key);
            }

            return new
            {
                applied = true,
                files = byFile.Count,
                breakpoints = breakpoints.Count,
                verified = verifiedCount,
                unverified,
                verdict_unknown = noVerdict,
                cleared
            };
        }
        catch (Exception ex)
        {
            return new { applied = false, reason = "dap_setBreakpoints_failed", error = ex.Message };
        }
    }

    /// <summary>Снять все живые точки: при очистке по всему workspace конкретный target не важен.</summary>
    static async Task<object> ClearAllLiveAsync(CancellationToken cancellationToken)
    {
        var client = DebugSession.CurrentClient;
        if (client is null)
            return new { applied = false, reason = "no_active_dap; will apply on next launch/attach" };

        ResetLiveBpIfNewSession(client);
        var cleared = new List<string>();
        foreach (var file in LiveBpFiles.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SetBreakpointsAsync(file, Array.Empty<(int, string?)>(), cancellationToken).ConfigureAwait(false);
            LiveBpFiles.Remove(file);
            cleared.Add(file);
        }

        return new { applied = true, cleared };
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
        if (target is null
            && string.Equals(session.Language, CdpLanguages.PowerShell, StringComparison.OrdinalIgnoreCase)
            && session.ProjectRoot is { Length: > 0 } pr
            && args is not null)
        {
            var pathArg = OptString(args, "path") ?? OptString(args, "file_path");
            if (pathArg is { Length: > 0 })
            {
                var resolved = Path.IsPathRooted(pathArg)
                    ? Path.GetFullPath(pathArg)
                    : Path.GetFullPath(Path.Combine(pr, pathArg));
                if (Ps1BufferDiagnostics.IsPs1Path(resolved))
                    target = resolved;
            }
        }

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
