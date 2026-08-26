using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Xunit;

namespace CdpMcp.Tests;

public sealed class WritingCanonStackTests
{
    [Fact]
    public void Build_uses_embedded_defaults_when_no_disk_toml()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var stack = WritingCanonStackResolver.Build(root);
            Assert.Equal(root, stack.ScmRoot);
            Assert.Equal("embedded", stack.SettingsSource);
            Assert.Contains(stack.Code, e => e.Layer == "project" && e.Path.EndsWith("canon.md", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_merges_disk_canon_section()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canon-test-{Guid.NewGuid():N}");
        var cdpDir = Path.Combine(root, ".cdp");
        Directory.CreateDirectory(cdpDir);
        File.WriteAllText(
            Path.Combine(cdpDir, "project.toml"),
            """
            [canon]
            lang = "csharp"
            org_style = "guiders-style@v1"
            canon_file = "canon.md"
            """);
        try
        {
            var stack = WritingCanonStackResolver.Build(root);
            Assert.Equal("embedded+disk", stack.SettingsSource);
            Assert.Contains(stack.Code, e => e.Layer == "org-lang" && e.Path.Contains("csharp", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IdeCanonChannel_requires_scm_root()
    {
        var json = IdeCanonChannel.HandleJson(new SessionContext(), new Dictionary<string, JsonElement>());
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("need_scm_root", doc.RootElement.GetProperty("error").GetString());
    }
}
