#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public class CitizenAiKeysTests
{
    [Fact]
    public void Parse_snake_case_keys()
    {
        var snap = CitizenAiKeys.Parse("""
            anthropic_api_key = "sk-ant-test-abcdefgh"
            open_ai_api_key = "sk-openai-xyz"
            deep_seek_api_key = ""
            open_ai_base_url = "https://foundation-models.api.cloud.ru/v1"
            open_ai_model = "Qwen/Qwen3-Coder-Next"
            """, "mem://test");

        Assert.Equal("sk-ant-test-abcdefgh", snap.AnthropicApiKey);
        Assert.Equal("sk-openai-xyz", snap.OpenAiApiKey);
        Assert.Null(snap.DeepSeekApiKey);
        Assert.Equal("https://foundation-models.api.cloud.ru/v1", snap.OpenAiBaseUrl);
        Assert.Equal("Qwen/Qwen3-Coder-Next", snap.OpenAiModel);
        Assert.True(snap.HasAny);
        Assert.True(snap.HasLiveProvider);
        Assert.True(snap.FileExists);
    }

    [Fact]
    public void Masked_never_echoes_full_secret()
    {
        Assert.Equal("missing", CitizenAiKeys.Masked(null));
        Assert.Equal("missing", CitizenAiKeys.Masked(""));
        Assert.Equal("set", CitizenAiKeys.Masked("short"));
        var m = CitizenAiKeys.Masked("sk-ant-test-abcdefgh");
        Assert.StartsWith("set…", m);
        Assert.DoesNotContain("sk-ant-test", m);
        Assert.EndsWith("efgh", m);
    }

    [Fact]
    public void ToPublicPulse_has_no_raw_keys()
    {
        var snap = CitizenAiKeys.Parse("anthropic_api_key = \"super-secret-key-value\"\n", "mem://x");
        var json = System.Text.Json.JsonSerializer.Serialize(snap.ToPublicPulse());
        Assert.DoesNotContain("super-secret", json);
        Assert.Contains("has_any", json);
    }

    [Fact]
    public void Load_missing_file_is_safe()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-citizen-keys-missing-" + Guid.NewGuid().ToString("N") + ".toml");
        var snap = CitizenAiKeys.Load(path);
        Assert.False(snap.FileExists);
        Assert.False(snap.HasAny);
        Assert.Equal(path, snap.Path);
    }
}
