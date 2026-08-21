using Cdp.Core;
using CdpMcp;
using Xunit;

namespace CdpMcp.Tests;

public sealed class Ps1EditorServicesTests
{
    [Fact]
    public void BuildLspPreset_powershell_id_and_language()
    {
        var preset = Ps1EditorServices.BuildLspPreset();
        Assert.Equal("powershell", preset.Id);
        Assert.Contains(CdpLanguages.PowerShell, preset.LanguageIds);
        Assert.Contains("-File", preset.Args);
    }

    [Fact]
    public void ResolveBootstrapScript_finds_editor_services_script()
    {
        var path = Ps1EditorServices.ResolveBootstrapScript("Start-PsEditorServices.ps1");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ResolvePses_probe_finds_vscode_extension_bundle()
    {
        var script = Ps1EditorServices.ResolveBootstrapScript("Resolve-PsEditorServices.ps1");
        var psi = new System.Diagnostics.ProcessStartInfo("pwsh", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Probe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30_000);
        Assert.Equal(0, proc.ExitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var source = doc.RootElement.GetProperty("source").GetString();
        Assert.True(source is "vscode-extension" or "module" or "env");
    }
}
