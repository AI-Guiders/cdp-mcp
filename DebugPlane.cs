using System.Text.Json;
using Cdp.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;

namespace CdpMcp;

/// <summary>
/// CDP debug plane: structured breakpoint + DAP control without "write the JSON file" mental model.
/// Persistence still uses BreakpointsStorage under the hood; agent surface is op=bp_add|…
/// </summary>
internal static class DebugPlane
{
    public static bool IsDebugPlaneTool(string name) =>
        name is "cdp_debug";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<string> DispatchAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var op = RequireString(args, "op").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" => Scene(session),
            "bp_set" => await BpSetAsync(session, args, cancellationToken).ConfigureAwait(false),
            "bp_add" => await BpAddAsync(session, args, cancellationToken).ConfigureAwait(false),
            "bp_remove" => await BpRemoveAsync(session, args, cancellationToken).ConfigureAwait(false),
            "bp_list" => BpList(session, args),
            "bp_clear" => BpClear(session, args),
            "launch" => await ForwardAsync(byDomain, "debug_launch", session, args, fillPaths: true, cancellationToken)
                .ConfigureAwait(false),
            "attach" => await ForwardAsync(byDomain, "debug_attach", session, args, fillPaths: true, cancellationToken)
                .ConfigureAwait(false),
            "continue" => await ForwardAsync(byDomain, "debug_continue", session, args, fillPaths: false, cancellationToken)
                .ConfigureAwait(false),
            "stop" => await ForwardAsync(byDomain, "debug_stop", session, args, fillPaths: false, cancellationToken)
                .ConfigureAwait(false),
            "stop_context" => await ForwardAsync(byDomain, "debug_stop_context", session, args, fillPaths: false, cancellationToken)
                .ConfigureAwait(false),
            "step_over" => await ForwardAsync(byDomain, "debug_step_over", session, args, fillPaths: false, cancellationToken)
                .ConfigureAwait(false),
            "step_into" => await ForwardAsync(byDomain, "debug_step_into", session, args, fillPaths: false, cancellationToken)
                .ConfigureAwait(false),
            "step_out" => await ForwardAsync(byDomain, "debug_step_out", session, args, fillPaths: false, cancellationToken)
                .ConfigureAwait(false),
            "stack" => await ForwardAsync(byDomain, "debug_stack_trace", session, args, fillPaths: false, cancellationToken)
                .ConfigureAwait(false),
            "variables" => await ForwardAsync(byDomain, "debug_variables", session, args, fillPaths: false, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentException(
                "cdp_debug op must be scene|bp_set|bp_add|bp_remove|bp_list|bp_clear|launch|attach|continue|stop|stop_context|step_over|step_into|step_out|stack|variables.")
        };
    }

    static string Scene(SessionContext session)
    {
        var (ws, target, note) = TryResolvePaths(session, emptyArgs: true);
        var bps = ws is { Length: > 0 } && target is { Length: > 0 }
            ? BreakpointsStorage.GetBreakpoints(ws, target)
            : Array.Empty<BreakpointsStorage.BreakpointEntry>();
        var launchPath = target is { Length: > 0 } ? LaunchTargetResolver.TryResolveBinary(target) : null;
        return JsonSerializer.Serialize(new
        {
            schema = "debug_scene/v0",
            ok = true,
            session = new
            {
                project_root = session.ProjectRoot,
                solution_or_project_path = session.SolutionOrProjectPath,
                language = session.Language
            },
            defaults = new
            {
                workspace_path = ws,
                target_path = target,
                project_path = target,
                launch_path = launchPath,
                note
            },
            active_dap = DebugSession.CurrentClient is not null,
            last_stopped_thread_id = DebugSession.LastStoppedThreadId,
            breakpoints = bps.Select(WireBp),
            hint =
                "Prefer bp_add path+line (session defaults after cdp_open). " +
                "target_path/.csproj is BP key; launch resolves launch_path (.dll) under bin/. JSON file is storage, not the API."
        }, Pretty);
    }

    static async Task<string> BpSetAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var (ws, target) = RequirePaths(session, args);
        if (!args.TryGetValue("breakpoints", out var bpEl) || bpEl.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("bp_set requires breakpoints=[{path|file_path,line,condition?}].");

        var list = new List<BreakpointsStorage.BreakpointEntry>();
        foreach (var item in bpEl.EnumerateArray())
        {
            var file = PropString(item, "path") ?? PropString(item, "file_path");
            if (file is null || !TryPropInt(item, "line", out var line) || line < 1)
                continue;
            var condition = PropString(item, "condition");
            list.Add(new BreakpointsStorage.BreakpointEntry(
                Path.GetFullPath(file), line, string.IsNullOrWhiteSpace(condition) ? null : condition));
        }

        BreakpointsStorage.SetBreakpoints(ws, target, list);
        var live = await TryApplyLiveAsync(ws, target, list, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            schema = "debug_bp_set/v0",
            ok = true,
            workspace_path = ws,
            target_path = target,
            count = list.Count,
            breakpoints = list.Select(WireBp),
            live_dap = live,
            hint = "Replaces all breakpoints for this target. Prefer bp_add for incremental edits."
        }, Pretty);
    }

    static async Task<string> BpAddAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var (ws, target) = RequirePaths(session, args);
        var path = OptString(args, "path") ?? OptString(args, "file_path")
            ?? throw new ArgumentException("bp_add requires path (or file_path).");
        var line = RequireInt(args, "line");
        if (line < 1)
            throw new ArgumentException("line must be >= 1.");
        var condition = OptString(args, "condition");
        var entry = new BreakpointsStorage.BreakpointEntry(
            Path.GetFullPath(path), line, string.IsNullOrWhiteSpace(condition) ? null : condition);
        var remaining = BreakpointsStorage.AddBreakpoint(ws, target, entry);
        var live = await TryApplyLiveAsync(ws, target, remaining, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            schema = "debug_bp_add/v0",
            ok = true,
            workspace_path = ws,
            target_path = target,
            added = WireBp(entry),
            count = remaining.Count,
            breakpoints = remaining.Select(WireBp),
            live_dap = live
        }, Pretty);
    }

    static async Task<string> BpRemoveAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var (ws, target) = RequirePaths(session, args);
        var path = OptString(args, "path") ?? OptString(args, "file_path")
            ?? throw new ArgumentException("bp_remove requires path (or file_path).");
        var line = RequireInt(args, "line");
        var remaining = BreakpointsStorage.RemoveBreakpoint(ws, target, path, line);
        var live = await TryApplyLiveAsync(ws, target, remaining, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            schema = "debug_bp_remove/v0",
            ok = true,
            workspace_path = ws,
            target_path = target,
            removed = new { path = Path.GetFullPath(path), line },
            count = remaining.Count,
            breakpoints = remaining.Select(WireBp),
            live_dap = live
        }, Pretty);
    }

    static string BpList(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var (ws, target, note) = TryResolvePaths(session, emptyArgs: false, args);
        if (ws is not { Length: > 0 })
            throw new ArgumentException("workspace_path required (or cdp_open first). " + note);

        if (target is { Length: > 0 })
        {
            var bps = BreakpointsStorage.GetBreakpoints(ws, target);
            return JsonSerializer.Serialize(new
            {
                schema = "debug_bp_list/v0",
                ok = true,
                workspace_path = ws,
                target_path = target,
                count = bps.Count,
                breakpoints = bps.Select(WireBp)
            }, Pretty);
        }

        var targets = BreakpointsStorage.ListTargets(ws);
        return JsonSerializer.Serialize(new
        {
            schema = "debug_bp_list/v0",
            ok = true,
            workspace_path = ws,
            targets = targets.Select(kv => new
            {
                target_path = kv.Key,
                count = kv.Value.Count,
                breakpoints = kv.Value.Select(WireBp)
            })
        }, Pretty);
    }

    static string BpClear(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var (ws, target) = RequirePaths(session, args, targetOptional: true);
        BreakpointsStorage.ClearBreakpoints(ws, target);
        return JsonSerializer.Serialize(new
        {
            schema = "debug_bp_clear/v0",
            ok = true,
            workspace_path = ws,
            target_path = target,
            cleared = target is { Length: > 0 } ? "target" : "workspace"
        }, Pretty);
    }

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
