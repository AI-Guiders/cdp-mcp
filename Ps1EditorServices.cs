#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Cdp.Lsp;

namespace CdpMcp;

/// <summary>PSES bootstrap paths and LSP preset for powershell first-class.</summary>
internal static class Ps1EditorServices
{
    public const string OpenVsxPluginId = "ms-vscode.powershell";

    public static LspLaunchPreset BuildLspPreset()
    {
        var pwsh = Ps1PwshRuntime.Resolve() ?? "pwsh";
        var script = ResolveBootstrapScript("Start-PsEditorServices.ps1");
        return new LspLaunchPreset
        {
            Id = "powershell",
            Command = pwsh,
            CommandCandidates = [pwsh, "pwsh", "pwsh.exe", "powershell", "powershell.exe"],
            Args = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script],
            LanguageIds = [Cdp.Core.CdpLanguages.PowerShell],
            RootMarkers = [".ps1", ".psm1", ".git"]
        };
    }

    public static string ResolveDebugAdapterScript() =>
        ResolveBootstrapScript("Start-PsDebugAdapter.ps1");

    public static bool TryProbe(out JsonDocument? probe)
    {
        probe = null;
        try
        {
            var script = ResolveBootstrapScript("Resolve-PsEditorServices.ps1");
            var pwsh = Ps1PwshRuntime.Resolve() ?? "pwsh";
            var psi = new ProcessStartInfo(pwsh, $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Probe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return false;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(30_000);
            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return false;
            probe = JsonDocument.Parse(stdout);
            return probe.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch
        {
            probe?.Dispose();
            probe = null;
            return false;
        }
    }

    public static EnsureResult EnsureOpenVsx(CancellationToken cancellationToken = default)
    {
        if (TryProbe(out _))
            return new EnsureResult(true, "already_ok", null, null, null);

        if (!OpenVsxClient.TryParseId(OpenVsxPluginId, out var ns, out var name))
            return new EnsureResult(false, "id_bad", "ms-vscode.powershell", null, null);

        var dl = OpenVsxClient.Download(ns, name, version: null, cancellationToken);
        if (!dl.Ok || dl.Path is not { Length: > 0 })
            return new EnsureResult(false, dl.Error ?? "download_failed", dl.Hint, null, dl.Meta?.Version);

        var installed = CdpPluginQuarantine.InstallFromVsix(dl.Path);
        if (!installed.Ok)
            return new EnsureResult(false, installed.Error, installed.Hint, installed.Plugin, dl.Meta?.Version);

        if (!TryProbe(out _))
        {
            return new EnsureResult(
                false,
                "installed_but_unresolved",
                "Open VSX install ok but PSES probe failed — check quarantine payload",
                installed.Plugin,
                installed.Plugin?.Version ?? dl.Meta?.Version);
        }

        return new EnsureResult(true, null, installed.Hint, installed.Plugin, installed.Plugin?.Version ?? dl.Meta?.Version);
    }

    public static string ResolveBootstrapScript(string fileName)
    {
        var candidates = new List<string>();
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is { Length: > 0 })
        {
            candidates.Add(Path.Combine(exeDir, "scripts", fileName));
            candidates.Add(Path.Combine(exeDir, fileName));
        }

        var asmDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(asmDir))
        {
            candidates.Add(Path.Combine(asmDir, "scripts", fileName));
            candidates.Add(Path.Combine(asmDir, fileName));
        }

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(c))
                return Path.GetFullPath(c);
        }

        throw new InvalidOperationException(
            $"Missing PSES bootstrap script '{fileName}' next to CdpMcp (scripts/{fileName}). Redeploy cdp-mcp.");
    }

    public sealed record EnsureResult(
        bool Ok,
        string? Error,
        string? Hint,
        CdpPluginQuarantine.PluginInfo? Plugin,
        string? Version);
}
