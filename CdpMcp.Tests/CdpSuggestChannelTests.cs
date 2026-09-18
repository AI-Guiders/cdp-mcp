using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpSuggestChannelTests
{
    static string Handle(IReadOnlyDictionary<string, JsonElement> args, SessionContext? session = null)
    {
        session ??= new SessionContext();
        return CdpSuggestChannel.HandleJson(session, args);
    }

    static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    static Dictionary<string, JsonElement> Args(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public void Scene_lists_six_attach_verbs()
    {
        var root = Parse(Handle(Args("""{ "op": "scene" }""")));
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(6, root.GetProperty("verbs").GetArrayLength());
        Assert.Equal("federation.attach.verb", root.GetProperty("verb_suggestion_id").GetString());
    }

    [Fact]
    public void Choices_verb_picker_partial_filters_document()
    {
        var root = Parse(Handle(Args("""{ "op": "choices", "partial": "doc" }""")));
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("federation.attach.verb", root.GetProperty("suggestion_id").GetString());
        var choices = root.GetProperty("choices");
        Assert.Equal(1, choices.GetArrayLength());
        Assert.Equal("document", choices[0].GetProperty("value").GetString());
    }

    [Fact]
    public void Choices_verb_code_defaults_to_pick_file_step()
    {
        var root = Parse(Handle(Args("""{ "op": "choices", "verb": "code" }""")));
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("federation.attach.step.pick_file", root.GetProperty("suggestion_id").GetString());
        Assert.Equal("code", root.GetProperty("verb").GetString());
    }

    [Fact]
    public void Choices_step_pick_kind_lists_relation_spec_cases()
    {
        var root = Parse(Handle(Args("""{ "op": "choices", "step": "pick_kind" }""")));
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("federation.attach.step.pick_kind", root.GetProperty("suggestion_id").GetString());
        Assert.Equal(6, root.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Choices_without_step_returns_verb_picker()
    {
        var root = Parse(Handle(Args("""{ "op": "choices" }""")));
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("federation.attach.verb", root.GetProperty("suggestion_id").GetString());
        Assert.Equal(6, root.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Meta_catalog_includes_cdp_suggest()
    {
        var tool = MetaToolCatalog.Build().Single(t => t.Name == "cdp_suggest");
        Assert.Contains("ADR-0229", tool.Description, StringComparison.OrdinalIgnoreCase);
    }
}
