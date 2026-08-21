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
}
