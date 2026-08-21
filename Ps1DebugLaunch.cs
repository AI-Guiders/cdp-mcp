#nullable enable
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotnetDebug.Core;
using DotnetDebugMcp;

namespace CdpMcp;

/// <summary>PowerShell debug via PSES DebugServiceOnly DAP (first-class .ps1, not netcoredbg).</summary>
internal static class Ps1DebugLaunch
{
    public static bool IsPs1Target(string? path) =>
        Ps1BufferDiagnostics.IsPs1Path(path);

    public static async Task<string> HandleLaunchAsync(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!McpArgumentHelpers.TryGetString(args, "workspace_path", out var workspacePath)
            || string.IsNullOrWhiteSpace(workspacePath))
            throw new ArgumentException("workspace_path is required.");
        if (!McpArgumentHelpers.TryGetString(args, "target_path", out var targetPath)
            || string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("target_path is required (.ps1 script).");

        var workspaceRoot = Path.GetFullPath(workspacePath!.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        var scriptFull = Path.IsPathRooted(targetPath!.Trim())
            ? Path.GetFullPath(targetPath)
            : Path.GetFullPath(Path.Combine(workspaceRoot, targetPath.Trim()));
        if (!File.Exists(scriptFull))
            throw new ArgumentException($"Script not found: {scriptFull}");

        var pwsh = Ps1PwshRuntime.Resolve()
            ?? throw new InvalidOperationException("pwsh missing — install PowerShell 7+ for PS debug.");
        var adapterScript = Ps1EditorServices.ResolveDebugAdapterScript();
        var psi = new ProcessStartInfo
        {
            FileName = pwsh,
            WorkingDirectory = workspaceRoot
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(adapterScript);

        var breakpoints = BreakpointsStorage.GetBreakpoints(workspacePath!, targetPath!).ToList();
        var byFile = breakpoints
            .GroupBy(b => Path.GetFullPath(b.File), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(b => (b.Line, b.Condition)).ToList());

        var client = await DapClient.StartAdapterAsync(psi, "powershell").ConfigureAwait(false);
        client.OnConnectionLost = () =>
        {
            if (DebugSession.CurrentClient == client)
                DebugSession.Clear();
        };
        DebugSession.PrepareStoppedWait();
        var stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnEvent = (eventName, body) =>
        {
            if (eventName == "stopped" && body.TryGetProperty("threadId", out var tid))
            {
                var reason = body.TryGetProperty("reason", out var r) ? r.GetString() : null;
                var exceptionText = reason == "exception" && body.TryGetProperty("text", out var txt)
                    ? txt.GetString()
                    : null;
                DebugSession.OnStopped(tid.GetInt32(), exceptionText);
                stoppedTcs.TrySetResult();
            }
            else if (eventName == "continued")
                DebugSession.OnContinued();
        };

        try
        {
            var cwd = Path.GetDirectoryName(scriptFull) ?? workspaceRoot;
            await client.LaunchPowerShellAsync(scriptFull, cwd).ConfigureAwait(false);
            foreach (var (file, list) in byFile)
            {
                if (list.Count > 0)
                    await client.SetBreakpointsAsync(file, list).ConfigureAwait(false);
            }

            await client.ConfigurationDoneAsync().ConfigureAwait(false);
            await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            stoppedTcs.TrySetResult();
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        DebugSession.CurrentClient = client;
        DebugSession.WorkspacePath = workspacePath!.Trim();
        DebugSession.TargetPath = targetPath!.Trim();

        var sb = new StringBuilder();
        sb.AppendLine("# PowerShell debug session (PSES DAP)");
        sb.AppendLine($"# Script: {scriptFull}");
        sb.AppendLine($"# Cwd: {Path.GetDirectoryName(scriptFull)}");
        sb.AppendLine($"# Breakpoints: {breakpoints.Count}");
        sb.AppendLine("# Prefer debug_stop_context after stopped.");
        return sb.ToString();
    }
}
