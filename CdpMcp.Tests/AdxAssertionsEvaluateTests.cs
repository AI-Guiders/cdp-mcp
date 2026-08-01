#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class AdxAssertionsEvaluateTests
{
    [Fact]
    public void Evaluate_LoadsCatalog_AndPassesKernels()
    {
        var root = FindRepoRoot();
        var board = AdxAssertions.Evaluate(root);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(board));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("assert", doc.RootElement.GetProperty("scope").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("deferred").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("fail").GetInt32());
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var csproj = Path.Combine(dir.FullName, "CdpMcp.csproj");
            if (File.Exists(csproj))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("CdpMcp.csproj not found from BaseDirectory");
    }
}
