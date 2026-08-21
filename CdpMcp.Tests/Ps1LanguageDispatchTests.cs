#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cdp.Core;
using Cdp.Lsp;
using Xunit;

namespace CdpMcp.Tests;

public sealed class Ps1LanguageDispatchTests
{
    [Fact]
    public async Task Get_diagnostics_uses_parser_even_when_lsp_preset_mounted()
    {
        if (Ps1PwshRuntime.Resolve() is null)
            return;

        var presets = new List<LspLaunchPreset> { Ps1EditorServices.BuildLspPreset() };
        IdeLanguageTools.Configure(LanguageRegistry.Default, presets);
        IdeLanguageTools.BindDocumentStore(null);

        var root = Path.Combine(Path.GetTempPath(), "cdp-ps1-dispatch-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "probe.ps1");
        await File.WriteAllTextAsync(path, "Write-Output 'ok'\n");

        try
        {
            var session = new SessionContext { ProjectRoot = root, Language = CdpLanguages.PowerShell };
            var raw = await IdeLanguageTools.DispatchBareAsync(
                "get_diagnostics",
                session,
                new Dictionary<string, ICdpBackendModule>(),
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["file_path"] = JsonSerializer.SerializeToElement(path),
                    ["language"] = JsonSerializer.SerializeToElement(CdpLanguages.PowerShell)
                },
                CancellationToken.None);

            using var doc = JsonDocument.Parse(raw);
            Assert.Equal("powershell.parser", doc.RootElement.GetProperty("kind").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            IdeLanguageTools.Configure(LanguageRegistry.Default);
        }
    }
}
