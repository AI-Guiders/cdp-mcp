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
    public void Build_uses_primary_knowledge_from_host_toml()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var kb = Path.Combine(Path.GetTempPath(), $"canon-kb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(kb, "knowledge", "personal"));
        try
        {
            var host = new WritingCanonHostPaths { PrimaryKnowledgeRoot = kb };
            var stack = WritingCanonStackResolver.Build(root, host);
            var personal = stack.Operator.Single(e => e.Layer == "personal");
            Assert.Contains("operator-writing-prefs.md", personal.Path, StringComparison.Ordinal);
            Assert.StartsWith(kb, personal.Path, StringComparison.Ordinal);
            Assert.Equal("agent-notes-mcp.toml+embedded", personal.Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(kb, recursive: true);
        }
    }

    [Fact]
    public void Build_uses_guiders_style_from_host_toml()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canon-test-{Guid.NewGuid():N}");
        var cdpDir = Path.Combine(root, ".cdp");
        Directory.CreateDirectory(cdpDir);
        File.WriteAllText(
            Path.Combine(cdpDir, "project.toml"),
            """
            [canon]
            lang = "csharp"
            """);
        var styleRoot = Path.Combine(Path.GetTempPath(), $"canon-style-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(styleRoot, "csharp"));
        try
        {
            var host = new WritingCanonHostPaths { GuidersStyleRoot = styleRoot };
            var stack = WritingCanonStackResolver.Build(root, host);
            var org = stack.Code.Single(e => e.Layer == "org-lang");
            Assert.Contains(styleRoot, org.Path, StringComparison.Ordinal);
            Assert.Equal("cdp-mcp.toml", org.Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(styleRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_infers_lang_from_session_when_not_in_project_toml()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var styleRoot = Path.Combine(Path.GetTempPath(), $"canon-style-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(styleRoot, "typescript"));
        try
        {
            var host = new WritingCanonHostPaths
            {
                SessionLanguage = "typescript",
                GuidersStyleRoot = styleRoot,
            };
            var stack = WritingCanonStackResolver.Build(root, host);
            Assert.Equal("typescript", stack.EffectiveLang);
            Assert.Equal("session", stack.LangSource);
            Assert.Contains(stack.Code, e => e.Layer == "org-lang" && e.Path.Contains("typescript", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(styleRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_infers_lang_from_buffer_when_session_unset()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var styleRoot = Path.Combine(Path.GetTempPath(), $"canon-style-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(styleRoot, "python"));
        try
        {
            var host = new WritingCanonHostPaths
            {
                BufferLanguage = "python",
                GuidersStyleRoot = styleRoot,
            };
            var stack = WritingCanonStackResolver.Build(root, host);
            Assert.Equal("python", stack.EffectiveLang);
            Assert.Equal("buffer", stack.LangSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(styleRoot, recursive: true);
        }
    }

    [Fact]
    public void IdeCanonChannel_requires_scm_root()
    {
        var json = IdeCanonChannel.HandleJson(
            new SessionContext(),
            new CdpSettings(),
            new DocumentBufferStore(),
            new Dictionary<string, JsonElement>());
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("need_scm_root", doc.RootElement.GetProperty("error").GetString());
    }
}
