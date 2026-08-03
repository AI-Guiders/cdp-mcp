#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeLanguageToolsLspHealthTests
{
    [Fact]
    public void LspHealth_pulse_omits_resolved_probe()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(IdeLanguageTools.LspHealth(resolveProbe: false)));
        Assert.False(doc.RootElement.GetProperty("probe").GetBoolean());
        foreach (var p in doc.RootElement.GetProperty("presets").EnumerateArray())
            Assert.False(p.TryGetProperty("resolved_probe", out _));
    }

    [Fact]
    public void LspHealth_full_includes_resolved_probe()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(IdeLanguageTools.LspHealth(resolveProbe: true)));
        Assert.True(doc.RootElement.GetProperty("probe").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("presets").GetArrayLength() > 0);
        Assert.True(doc.RootElement.GetProperty("presets")[0].TryGetProperty("resolved_probe", out _));
    }
}
