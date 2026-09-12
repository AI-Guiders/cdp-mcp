#nullable enable
using System.Text.Json;
using AgentNotes.Core;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>CDP-ADR-0218 recall_knowledge facade (live primary KB when configured).</summary>
public sealed class RecallKnowledgeFacadeTests
{
    const string ConfigPath = @"D:/agent-notes-mcp/agent-notes-mcp.toml";

    static bool TryInit(out NotesStorage storage)
    {
        storage = new NotesStorage();
        if (!File.Exists(ConfigPath))
            return false;
        var code = AgentNotesBootstrap.TryLoadSettings(["--config", ConfigPath], out var settings, out _);
        if (code != 0 || settings is null)
            return false;
        AgentNotesRuntime.Initialize(settings, AgentNotesBootstrap.LoadedConfigPath);
        return true;
    }

    static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void Auto_ai_incidents_corpus_lookup_skips_hot()
    {
        if (!TryInit(out var storage))
            return;

        using var doc = Parse(storage.RecallKnowledge("ai-incidents"));
        var root = doc.RootElement;
        var layers = Layers(root).ToArray();
        Assert.Contains("corpus", layers);
        Assert.True(layers.Contains("lookup") || layers.Contains("search") || layers.Contains("resolve"));
        Assert.DoesNotContain("hot", layers);
        Assert.False(root.TryGetProperty("hot", out _));
        Assert.Contains(
            root.GetProperty("hits").EnumerateArray(),
            h => (h.GetProperty("path").GetString() ?? "").Contains("personal/ai-incidents", StringComparison.Ordinal));
    }

    [Fact]
    public void Auto_pml_uses_resolve_path()
    {
        if (!TryInit(out var storage))
            return;

        using var doc = Parse(storage.RecallKnowledge("PML"));
        var layers = Layers(doc.RootElement);
        Assert.Contains("resolve", layers);
        Assert.True(doc.RootElement.GetProperty("total").GetInt32() > 0);
    }

    [Fact]
    public void Auto_hot_only_query_falls_through_to_hot()
    {
        if (!TryInit(out var storage))
            return;

        var hotProbe = Guid.NewGuid().ToString("N");
        storage.Append("", $"\n<!-- section:recall-facade-probe -->\n{hotProbe}\n<!-- /section:recall-facade-probe -->\n");

        using var doc = Parse(storage.RecallKnowledge(hotProbe, layer: "auto", limit: 5));
        Assert.Contains("hot", Layers(doc.RootElement));
        Assert.True(doc.RootElement.TryGetProperty("hot", out _));
    }

    [Fact]
    public void Layer_corpus_never_includes_hot()
    {
        if (!TryInit(out var storage))
            return;

        using var doc = Parse(storage.RecallKnowledge("ai-incidents", layer: "corpus"));
        Assert.DoesNotContain("hot", Layers(doc.RootElement));
        Assert.False(doc.RootElement.TryGetProperty("hot", out _));
    }

    [Fact]
    public void Layer_hot_never_includes_corpus_steps()
    {
        if (!TryInit(out var storage))
            return;

        using var doc = Parse(storage.RecallKnowledge("agent-notes", layer: "hot"));
        var layers = Layers(doc.RootElement);
        Assert.Contains("hot", layers);
        Assert.DoesNotContain("corpus", layers);
        Assert.DoesNotContain("lookup", layers);
        Assert.DoesNotContain("search", layers);
    }

    [Fact]
    public void Scope_first_ranks_cascade_ide()
    {
        if (!TryInit(out var storage))
            return;

        using var doc = Parse(storage.RecallKnowledge(
            "cdp",
            activeScope: "door-to-singularity",
            limit: 15));
        var first = doc.RootElement.GetProperty("hits").EnumerateArray().FirstOrDefault();
        Assert.NotEqual(default, first);
        var path = first.GetProperty("path").GetString() ?? "";
        Assert.Contains("door-to-singularity", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_empty_includes_try_next_recall()
    {
        if (!TryInit(out var storage))
            return;

        using var doc = Parse(storage.Search("", "zzzz-no-such-corpus-topic-0218", 5));
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("total_matches").GetInt32());
        Assert.Equal("hot", root.GetProperty("layer").GetString());
        Assert.True(root.TryGetProperty("try_next", out var next));
        Assert.Equal("memory_world_recall_knowledge", next.GetProperty("tool").GetString());
    }

    static IEnumerable<string> Layers(JsonElement root)
    {
        if (!root.TryGetProperty("layers_used", out var layers) || layers.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var l in layers.EnumerateArray())
        {
            if (l.ValueKind == JsonValueKind.String && l.GetString() is { } s)
                yield return s;
        }
    }
}
