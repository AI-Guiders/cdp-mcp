#nullable enable
using Cdp.Lsp;

namespace CdpMcp;

/// <summary>PSES bootstrap paths and LSP preset for powershell first-class.</summary>
internal static class Ps1EditorServices
{
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
}
